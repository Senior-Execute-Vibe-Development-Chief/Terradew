using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Terradew.Core.Protocol;

/// <summary>
/// Frame layout on the socket: int32 payload length, then one byte message type, then the payload.
/// Everything is little endian, as BinaryWriter writes it.
/// </summary>
public static class Wire
{
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public static byte[] Encode(Message message)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)message.Type);
            message.Write(writer);
        }
        byte[] frame = new byte[4 + body.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), (int)body.Length);
        body.Position = 0;
        body.Read(frame, 4, (int)body.Length);
        return frame;
    }

    /// <summary>Decodes one payload (type byte included, length prefix excluded).</summary>
    public static Message Decode(byte[] payload, int count)
    {
        using var stream = new MemoryStream(payload, 0, count, writable: false);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var type = (MsgType)reader.ReadByte();
        return Message.Read(type, reader);
    }

    public static void WriteBits(this BinaryWriter writer, IReadOnlyList<bool> bits)
    {
        writer.Write(bits.Count);
        int bytes = (bits.Count + 7) / 8;
        var packed = new byte[bytes];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                packed[i >> 3] |= (byte)(1 << (i & 7));
        }
        writer.Write(packed);
    }

    public static bool[] ReadBits(this BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxFrameBytes * 8)
            throw new InvalidDataException("Bad bit count.");
        byte[] packed = reader.ReadBytes((count + 7) / 8);
        var bits = new bool[count];
        for (int i = 0; i < count; i++)
            bits[i] = (packed[i >> 3] & (1 << (i & 7))) != 0;
        return bits;
    }

    public static void WriteList<T>(this BinaryWriter writer, IReadOnlyList<T> items, Action<BinaryWriter, T> write)
    {
        writer.Write(items.Count);
        for (int i = 0; i < items.Count; i++)
            write(writer, items[i]);
    }

    public static List<T> ReadList<T>(this BinaryReader reader, Func<BinaryReader, T> read, int max = 100_000)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max)
            throw new InvalidDataException("Bad list count.");
        var items = new List<T>(count);
        for (int i = 0; i < count; i++)
            items.Add(read(reader));
        return items;
    }

    public static float Finite(float value) => float.IsFinite(value) ? value : 0f;
}
