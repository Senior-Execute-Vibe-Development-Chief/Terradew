using System;
using System.IO;

namespace Terradew.Core;

/// <summary>
/// The Stardew tiles the farmer cannot walk through, one bit per tile.
/// Terraria stamps these as solid blocks.
/// </summary>
public static class CollisionMap
{
    public const int Magic = 0x4C4F4354;
    public const int Version = 1;

    public readonly record struct Grid(int Sequence, int LocationHash, int Width, int Height, bool[] Blocked);

    public static string Path => System.IO.Path.Combine(BridgeFile.Root, "collision.bin");

    public static int Publish(int locationHash, int width, int height, bool[] blocked, string? path = null)
    {
        if (width <= 0 || height <= 0 || blocked.Length != width * height)
            return 0;
        int checksum = Checksum(locationHash, width, height, blocked);
        if (checksum == lastChecksum && locationHash == lastHash)
            return lastSequence;
        lastChecksum = checksum;
        lastHash = locationHash;
        lastSequence++;
        int byteCount = (blocked.Length + 7) / 8;
        byte[] file = new byte[24 + byteCount];
        WriteInt(file, 0, Magic);
        WriteInt(file, 4, Version);
        WriteInt(file, 8, lastSequence);
        WriteInt(file, 12, locationHash);
        WriteInt(file, 16, width);
        WriteInt(file, 20, height);
        for (int i = 0; i < blocked.Length; i++)
        {
            if (blocked[i])
                file[24 + (i >> 3)] |= (byte)(1 << (i & 7));
        }
        string final = path ?? Path;
        string directory = System.IO.Path.GetDirectoryName(final) ?? "";
        if (directory.Length > 0)
            Directory.CreateDirectory(directory);
        string partial = final + ".partial";
        File.WriteAllBytes(partial, file);
        File.Move(partial, final, overwrite: true);
        return lastSequence;
    }

    public static bool TryRead(out Grid grid, string? path = null)
    {
        grid = default;
        string final = path ?? Path;
        if (!File.Exists(final))
            return false;
        byte[] file;
        try
        {
            file = File.ReadAllBytes(final);
        }
        catch (IOException)
        {
            return false;
        }
        if (file.Length < 24)
            return false;
        if (ReadInt(file, 0) != Magic || ReadInt(file, 4) != Version)
            return false;
        int sequence = ReadInt(file, 8);
        int hash = ReadInt(file, 12);
        int width = ReadInt(file, 16);
        int height = ReadInt(file, 20);
        if (width <= 0 || height <= 0)
            return false;
        int count = width * height;
        if (file.Length < 24 + (count + 7) / 8)
            return false;
        bool[] blocked = new bool[count];
        for (int i = 0; i < count; i++)
            blocked[i] = (file[24 + (i >> 3)] & (1 << (i & 7))) != 0;
        grid = new Grid(sequence, hash, width, height, blocked);
        return sequence > 0;
    }

    private static int lastChecksum;
    private static int lastHash;
    private static int lastSequence;

    private static int Checksum(int locationHash, int width, int height, bool[] blocked)
    {
        int hash = locationHash ^ (width * 397) ^ (height * 17);
        for (int i = 0; i < blocked.Length; i++)
        {
            if (blocked[i])
                hash = unchecked(hash * 31 + i);
        }
        return hash;
    }

    private static void WriteInt(byte[] file, int offset, int value)
    {
        file[offset] = (byte)value;
        file[offset + 1] = (byte)(value >> 8);
        file[offset + 2] = (byte)(value >> 16);
        file[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt(byte[] file, int offset)
    {
        return file[offset]
            | (file[offset + 1] << 8)
            | (file[offset + 2] << 16)
            | (file[offset + 3] << 24);
    }
}
