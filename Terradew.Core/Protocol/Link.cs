using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Terradew.Core.Protocol;

/// <summary>
/// One localhost socket between the two games. Terraria listens, Stardew connects.
/// Reads and writes happen on background threads; the game thread only enqueues and drains,
/// so neither game ever waits on the other.
/// </summary>
public sealed class Link : IDisposable
{
    public const int DefaultPort = 47821;
    private const int MaxQueued = 4096;

    private readonly bool listen;
    private readonly int port;
    private readonly ConcurrentQueue<Message> inbox = new();
    private readonly BlockingCollection<byte[]> outbox = new(new ConcurrentQueue<byte[]>(), MaxQueued);
    private readonly Thread worker;
    private readonly object socketGate = new();
    private volatile bool stopping;
    private volatile bool connected;
    private TcpClient? client;
    private TcpListener? listener;
    private long lastReceivedMs;
    private int connectionCount;

    private Link(bool listen, int port)
    {
        this.listen = listen;
        this.port = port;
        worker = new Thread(Run) { IsBackground = true, Name = listen ? "Terradew link (listen)" : "Terradew link (connect)" };
        worker.Start();
    }

    /// <summary>Terraria side.</summary>
    public static Link Listen(int port = DefaultPort) => new(true, port);

    /// <summary>Stardew side. Keeps retrying until Terraria is up.</summary>
    public static Link Connect(int port = DefaultPort) => new(false, port);

    public bool Connected => connected;

    /// <summary>Bumps every time a new connection is made, so callers can resend handshakes and pending state.</summary>
    public int ConnectionCount => Volatile.Read(ref connectionCount);

    public long LastReceivedMs => Interlocked.Read(ref lastReceivedMs);

    public string LastError { get; private set; } = "";

    /// <summary>Queue a message. Dropped if not connected, or if the queue is full.</summary>
    public bool Send(Message message)
    {
        if (!connected || stopping)
            return false;
        byte[] frame;
        try
        {
            frame = Wire.Encode(message);
        }
        catch (Exception error)
        {
            LastError = error.Message;
            return false;
        }
        return outbox.TryAdd(frame);
    }

    public bool TryReceive(out Message message)
    {
        if (inbox.TryDequeue(out Message? next))
        {
            message = next;
            return true;
        }
        message = null!;
        return false;
    }

    public void Dispose()
    {
        stopping = true;
        connected = false;
        lock (socketGate)
        {
            try { client?.Close(); } catch (Exception) { }
            try { listener?.Stop(); } catch (Exception) { }
        }
        outbox.CompleteAdding();
    }

    private void Run()
    {
        while (!stopping)
        {
            TcpClient? next = null;
            try
            {
                next = listen ? AcceptOne() : ConnectOnce();
            }
            catch (Exception error)
            {
                LastError = error.Message;
            }
            if (next == null)
            {
                Sleep(500);
                continue;
            }

            next.NoDelay = true;
            lock (socketGate)
                client = next;
            while (outbox.TryTake(out _))
            {
                // Anything queued for an older connection is stale.
            }
            while (inbox.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref lastReceivedMs, NowMs());
            connected = true;
            Interlocked.Increment(ref connectionCount);

            var writer = new Thread(() => WriteLoop(next)) { IsBackground = true, Name = "Terradew link (write)" };
            writer.Start();
            ReadLoop(next);
            connected = false;
            try { next.Close(); } catch (Exception) { }
            writer.Join(1000);
        }
    }

    private TcpClient? AcceptOne()
    {
        if (listener == null)
        {
            var created = new TcpListener(IPAddress.Loopback, port);
            created.Start();
            lock (socketGate)
                listener = created;
        }
        return listener.AcceptTcpClient();
    }

    private TcpClient? ConnectOnce()
    {
        var attempt = new TcpClient();
        try
        {
            attempt.Connect(IPAddress.Loopback, port);
            return attempt;
        }
        catch (SocketException)
        {
            attempt.Dispose();
            return null;
        }
    }

    private void ReadLoop(TcpClient socket)
    {
        try
        {
            NetworkStream stream = socket.GetStream();
            byte[] header = new byte[4];
            byte[] payload = new byte[64 * 1024];
            while (!stopping)
            {
                if (!ReadExact(stream, header, 4))
                    return;
                int length = BitConverter.ToInt32(header, 0);
                if (length <= 0 || length > Wire.MaxFrameBytes)
                {
                    LastError = $"Bad frame length {length}.";
                    return;
                }
                if (payload.Length < length)
                    payload = new byte[length];
                if (!ReadExact(stream, payload, length))
                    return;
                Interlocked.Exchange(ref lastReceivedMs, NowMs());
                Message message;
                try
                {
                    message = Wire.Decode(payload, length);
                }
                catch (Exception error)
                {
                    // One bad frame is skipped; the length prefix keeps the stream aligned.
                    LastError = error.Message;
                    continue;
                }
                if (inbox.Count < MaxQueued)
                    inbox.Enqueue(message);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            LastError = error.Message;
        }
    }

    private void WriteLoop(TcpClient socket)
    {
        try
        {
            NetworkStream stream = socket.GetStream();
            while (!stopping && connected)
            {
                if (!outbox.TryTake(out byte[]? frame, 250))
                    continue;
                stream.Write(frame, 0, frame.Length);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            LastError = error.Message;
            connected = false;
            try { socket.Close(); } catch (Exception) { }
        }
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0)
                return false;
            read += n;
        }
        return true;
    }

    private void Sleep(int ms)
    {
        for (int waited = 0; waited < ms && !stopping; waited += 50)
            Thread.Sleep(50);
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
