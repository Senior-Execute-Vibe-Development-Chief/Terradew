using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Terradew.Core;

[Flags]
public enum BridgeFlags
{
    None = 0,
    Town = 1,
    Boss = 2,
    Worm = 4,
    Glider = 8,
    Lurk = 16,
}

public readonly record struct BridgeEntity(
    int NpcId,
    int NpcType,
    float X,
    float Y,
    int Life,
    int LifeMax,
    BridgeFlags Flags,
    string Name,
    int ContactDamage = 0,
    short FrameX = 0,
    short FrameY = 0,
    short FrameW = 0,
    short FrameH = 0,
    int Direction = 1);

public readonly record struct BridgeEquipment(
    int HeldItem,
    int HeadItem,
    int BodyItem,
    int LegItem,
    int HeadSlot,
    int BodySlot,
    int LegSlot,
    int BodyVariant = 0);

public readonly record struct BridgeHit(int Serial, int NpcId, int Damage, int Direction);

public readonly record struct StardewPulse(float X, float Y, int LocationHash);

public readonly record struct BridgeSnapshot(
    int StardewSequence,
    int TerrariaSequence,
    long StardewUnixMs,
    long TerrariaUnixMs,
    float StardewX,
    float StardewY,
    int LocationHash,
    float TerrariaPlayerX,
    float TerrariaPlayerY,
    int RegionX,
    int RegionY,
    bool TerrariaStable,
    IReadOnlyList<BridgeEntity> Entities,
    BridgeEquipment Equipment)
{
    public bool TerrariaLinked(long nowUnixMs, long windowMs = 2000)
        => TerrariaStable && TerrariaSequence >= 2 && nowUnixMs - TerrariaUnixMs <= windowMs;

    public bool StardewLinked(long nowUnixMs, long windowMs = 2000)
        => StardewSequence >= 2 && (StardewSequence & 1) == 0 && nowUnixMs - StardewUnixMs <= windowMs;
}

/// <summary>
/// One file both games keep open. Each side writes only its own fields.
/// A sequence goes odd while a write is in progress and even when the frame is safe to read.
/// </summary>
public sealed class BridgeFile : IDisposable
{
    public const int Magic = 0x31574454;
    public const int Version = 4;
    public const int MaxEntities = 64;
    public const int MaxHits = 8;
    public const int NameBytes = 48;
    public const int EntityBytes = 96;
    public const int HitBytes = 16;
    public const int HitsAt = 64 + MaxEntities * EntityBytes;
    public const int EquipmentBytes = 32;
    public const int EquipmentAt = HitsAt + MaxHits * HitBytes;
    public const int FileBytes = EquipmentAt + EquipmentBytes;

    private const int StardewSequenceAt = 8;
    private const int TerrariaSequenceAt = 12;
    private const int StardewUnixAt = 16;
    private const int TerrariaUnixAt = 24;
    private const int EntityCountAt = 32;
    private const int StardewXAt = 36;
    private const int StardewYAt = 40;
    private const int LocationHashAt = 44;
    private const int TerrariaPlayerXAt = 48;
    private const int TerrariaPlayerYAt = 52;
    private const int RegionXAt = 56;
    private const int RegionYAt = 60;
    private const int EntitiesAt = 64;

    private static readonly Mutex Gate = new(false, "Terradew.Bridge");

    private readonly FileStream stream;
    private readonly byte[] scratch = new byte[FileBytes];
    private bool disposed;

    private BridgeFile(FileStream stream)
    {
        this.stream = stream;
    }

    public static string Root
    {
        get
        {
            // Stardew's runtime maps ApplicationData to ~/.config. tModLoader's runtime maps
            // it to ~/Library/Application Support. Both games have to share one folder.
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "Terradew");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Terradew");
        }
    }

    public static string DefaultPath => Path.Combine(Root, "bridge.bin");

    public static BridgeFile Open(string? path = null)
    {
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (stream.Length < FileBytes)
            stream.SetLength(FileBytes);
        var bridge = new BridgeFile(stream);
        bridge.EnsureHeader();
        return bridge;
    }

    public void WriteStardew(StardewPulse pulse)
    {
        WithGate(() =>
        {
            int sequence = ReadInt(StardewSequenceAt);
            WriteInt(StardewSequenceAt, sequence + 1);
            WriteInt64(StardewUnixAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            WriteFloat(StardewXAt, pulse.X);
            WriteFloat(StardewYAt, pulse.Y);
            WriteInt(LocationHashAt, pulse.LocationHash);
            WriteInt(StardewSequenceAt, sequence + 2);
            stream.Flush(true);
        });
    }

    public void TouchTerraria()
    {
        WithGate(() =>
        {
            int sequence = ReadInt(TerrariaSequenceAt);
            if (sequence < 2 || (sequence & 1) == 1)
                return;
            WriteInt64(TerrariaUnixAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            stream.Flush(true);
        });
    }

    public void WriteTerraria(float playerX, float playerY, IReadOnlyList<BridgeEntity> entities, BridgeEquipment equipment = default, int regionX = 0, int regionY = 0)
    {
        WithGate(() =>
        {
            int sequence = ReadInt(TerrariaSequenceAt);
            WriteInt(TerrariaSequenceAt, sequence + 1);
            WriteInt64(TerrariaUnixAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            WriteFloat(TerrariaPlayerXAt, playerX);
            WriteFloat(TerrariaPlayerYAt, playerY);
            WriteInt(RegionXAt, regionX);
            WriteInt(RegionYAt, regionY);
            int count = Math.Min(entities.Count, MaxEntities);
            WriteInt(EntityCountAt, count);
            for (int i = 0; i < count; i++)
                WriteEntity(EntitiesAt + i * EntityBytes, entities[i]);
            WriteEquipment(equipment);
            WriteInt(TerrariaSequenceAt, sequence + 2);
            stream.Flush(true);
        });
    }

    public BridgeSnapshot Read()
    {
        return WithGate(() =>
        {
            stream.Position = 0;
            ReadExact(scratch);
            int magic = BitConverter.ToInt32(scratch, 0);
            int version = BitConverter.ToInt32(scratch, 4);
            if (magic != Magic || version != Version)
                return Empty();

            int terrariaSequence = BitConverter.ToInt32(scratch, TerrariaSequenceAt);
            bool stable = terrariaSequence >= 2 && (terrariaSequence & 1) == 0;
            int count = stable ? Math.Clamp(BitConverter.ToInt32(scratch, EntityCountAt), 0, MaxEntities) : 0;
            var entities = new BridgeEntity[count];
            for (int i = 0; i < count; i++)
                entities[i] = ReadEntity(EntitiesAt + i * EntityBytes);

            return new BridgeSnapshot(
                BitConverter.ToInt32(scratch, StardewSequenceAt),
                terrariaSequence,
                BitConverter.ToInt64(scratch, StardewUnixAt),
                BitConverter.ToInt64(scratch, TerrariaUnixAt),
                BitConverter.ToSingle(scratch, StardewXAt),
                BitConverter.ToSingle(scratch, StardewYAt),
                BitConverter.ToInt32(scratch, LocationHashAt),
                BitConverter.ToSingle(scratch, TerrariaPlayerXAt),
                BitConverter.ToSingle(scratch, TerrariaPlayerYAt),
                BitConverter.ToInt32(scratch, RegionXAt),
                BitConverter.ToInt32(scratch, RegionYAt),
                stable,
                entities,
                stable ? ReadEquipment() : default);
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        stream.Dispose();
    }

    private void EnsureHeader()
    {
        WithGate(() =>
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[8];
            ReadExact(stream, header);
            if (BitConverter.ToInt32(header) == Magic && BitConverter.ToInt32(header[4..]) == Version)
                return;
            Array.Clear(scratch);
            BitConverter.TryWriteBytes(scratch.AsSpan(0), Magic);
            BitConverter.TryWriteBytes(scratch.AsSpan(4), Version);
            stream.Position = 0;
            stream.Write(scratch, 0, FileBytes);
            stream.Flush(true);
        });
    }

    private void WriteEntity(int offset, BridgeEntity entity)
    {
        WriteInt(offset, entity.NpcId);
        WriteInt(offset + 4, entity.NpcType);
        WriteFloat(offset + 8, entity.X);
        WriteFloat(offset + 12, entity.Y);
        WriteInt(offset + 16, entity.Life);
        WriteInt(offset + 20, entity.LifeMax);
        WriteInt(offset + 24, (int)entity.Flags);
        Span<byte> staged = stackalloc byte[NameBytes];
        WriteName(staged, entity.Name);
        stream.Position = offset + 28;
        stream.Write(staged);
        WriteInt(offset + 76, entity.ContactDamage);
        WriteInt16(offset + 80, entity.FrameX);
        WriteInt16(offset + 82, entity.FrameY);
        WriteInt16(offset + 84, entity.FrameW);
        WriteInt16(offset + 86, entity.FrameH);
        WriteInt(offset + 88, entity.Direction);
    }

    private void WriteEquipment(BridgeEquipment equipment)
    {
        WriteInt(EquipmentAt, equipment.HeldItem);
        WriteInt(EquipmentAt + 4, equipment.HeadItem);
        WriteInt(EquipmentAt + 8, equipment.BodyItem);
        WriteInt(EquipmentAt + 12, equipment.LegItem);
        WriteInt(EquipmentAt + 16, equipment.HeadSlot);
        WriteInt(EquipmentAt + 20, equipment.BodySlot);
        WriteInt(EquipmentAt + 24, equipment.LegSlot);
        WriteInt(EquipmentAt + 28, equipment.BodyVariant);
    }

    public void WriteHit(BridgeHit hit)
    {
        if (hit.Serial <= 0)
            return;
        int slot = (hit.Serial / 2) % MaxHits;
        int offset = HitsAt + slot * HitBytes;
        WithGate(() =>
        {
            WriteInt(offset, 0);
            WriteInt(offset + 4, hit.NpcId);
            WriteInt(offset + 8, hit.Damage);
            WriteInt(offset + 12, hit.Direction);
            WriteInt(offset, hit.Serial);
            stream.Flush(true);
        });
    }

    public IReadOnlyList<BridgeHit> ReadHits()
    {
        return WithGate(() =>
        {
            var hits = new List<BridgeHit>(MaxHits);
            for (int i = 0; i < MaxHits; i++)
            {
                int offset = HitsAt + i * HitBytes;
                int serial = ReadInt(offset);
                if (serial <= 0)
                    continue;
                hits.Add(new BridgeHit(serial, ReadInt(offset + 4), ReadInt(offset + 8), ReadInt(offset + 12)));
            }
            return hits;
        });
    }

    private BridgeEntity ReadEntity(int offset)
    {
        string name = Encoding.UTF8.GetString(scratch, offset + 28, NameBytes).TrimEnd('\0');
        return new BridgeEntity(
            BitConverter.ToInt32(scratch, offset),
            BitConverter.ToInt32(scratch, offset + 4),
            BitConverter.ToSingle(scratch, offset + 8),
            BitConverter.ToSingle(scratch, offset + 12),
            BitConverter.ToInt32(scratch, offset + 16),
            BitConverter.ToInt32(scratch, offset + 20),
            (BridgeFlags)BitConverter.ToInt32(scratch, offset + 24),
            name,
            BitConverter.ToInt32(scratch, offset + 76),
            BitConverter.ToInt16(scratch, offset + 80),
            BitConverter.ToInt16(scratch, offset + 82),
            BitConverter.ToInt16(scratch, offset + 84),
            BitConverter.ToInt16(scratch, offset + 86),
            BitConverter.ToInt32(scratch, offset + 88));
    }

    private BridgeEquipment ReadEquipment()
        => new(
            BitConverter.ToInt32(scratch, EquipmentAt),
            BitConverter.ToInt32(scratch, EquipmentAt + 4),
            BitConverter.ToInt32(scratch, EquipmentAt + 8),
            BitConverter.ToInt32(scratch, EquipmentAt + 12),
            BitConverter.ToInt32(scratch, EquipmentAt + 16),
            BitConverter.ToInt32(scratch, EquipmentAt + 20),
            BitConverter.ToInt32(scratch, EquipmentAt + 24),
            BitConverter.ToInt32(scratch, EquipmentAt + 28));

    private static void WriteName(Span<byte> dest, string? name)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(name))
            return;
        int written = 0;
        Span<byte> encoded = stackalloc byte[4];
        foreach (Rune rune in name.EnumerateRunes())
        {
            int count = rune.EncodeToUtf8(encoded);
            if (written + count > dest.Length)
                break;
            encoded[..count].CopyTo(dest[written..]);
            written += count;
        }
    }

    private int ReadInt(int offset)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[4];
        ReadExact(stream, bytes);
        return BitConverter.ToInt32(bytes);
    }

    private void WriteInt16(int offset, short value)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[2];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private void WriteInt(int offset, int value)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private void WriteInt64(int offset, long value)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private void WriteFloat(int offset, float value)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private void ReadExact(byte[] buffer)
    {
        stream.Position = 0;
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                throw new EndOfStreamException("Terradew bridge ended early.");
            read += n;
        }
    }

    private static void ReadExact(Stream source, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = source.Read(buffer[read..]);
            if (n == 0)
                throw new EndOfStreamException("Terradew bridge ended early.");
            read += n;
        }
    }

    private static void WithGate(Action action)
    {
        Wait();
        try
        {
            action();
        }
        finally
        {
            Gate.ReleaseMutex();
        }
    }

    private static T WithGate<T>(Func<T> action)
    {
        Wait();
        try
        {
            return action();
        }
        finally
        {
            Gate.ReleaseMutex();
        }
    }

    private static void Wait()
    {
        try
        {
            Gate.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The other game quit while holding the bridge. This process owns it now.
        }
    }

    private static BridgeSnapshot Empty()
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, Array.Empty<BridgeEntity>(), default);
}
