using System;
using System.Collections.Generic;
using System.IO;

namespace Terradew.Core;

/// <summary>
/// The next open tile for each ground mob, from Stardew's path search.
/// Tile coordinates are on the farmer's current map.
/// </summary>
public static class RouteMap
{
    public const int Magic = 0x54415054;
    public const int Version = 1;
    public const int MaxSteps = 64;

    public readonly record struct Step(int NpcId, int TileX, int TileY);

    public readonly record struct Page(int Sequence, int LocationHash, Step[] Steps);

    public static string Path => System.IO.Path.Combine(BridgeFile.Root, "routes.bin");

    public static int Publish(int locationHash, IReadOnlyList<Step> steps, string? path = null)
    {
        int count = Math.Min(steps.Count, MaxSteps);
        int checksum = Checksum(locationHash, steps, count);
        if (path == null && checksum == lastChecksum && locationHash == lastHash)
            return lastSequence;
        lastChecksum = checksum;
        lastHash = locationHash;
        lastSequence++;
        byte[] file = new byte[20 + count * 12];
        WriteInt(file, 0, Magic);
        WriteInt(file, 4, Version);
        WriteInt(file, 8, lastSequence);
        WriteInt(file, 12, locationHash);
        WriteInt(file, 16, count);
        for (int i = 0; i < count; i++)
        {
            int offset = 20 + i * 12;
            WriteInt(file, offset, steps[i].NpcId);
            WriteInt(file, offset + 4, steps[i].TileX);
            WriteInt(file, offset + 8, steps[i].TileY);
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

    public static bool TryRead(out Page page, string? path = null)
    {
        page = default;
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
        if (file.Length < 20)
            return false;
        if (ReadInt(file, 0) != Magic || ReadInt(file, 4) != Version)
            return false;
        int sequence = ReadInt(file, 8);
        int hash = ReadInt(file, 12);
        int count = ReadInt(file, 16);
        if (count < 0 || count > MaxSteps || file.Length < 20 + count * 12)
            return false;
        var steps = new Step[count];
        for (int i = 0; i < count; i++)
        {
            int offset = 20 + i * 12;
            steps[i] = new Step(ReadInt(file, offset), ReadInt(file, offset + 4), ReadInt(file, offset + 8));
        }
        page = new Page(sequence, hash, steps);
        return sequence > 0;
    }

    private static int lastChecksum;
    private static int lastHash;
    private static int lastSequence;

    private static int Checksum(int locationHash, IReadOnlyList<Step> steps, int count)
    {
        int hash = locationHash;
        for (int i = 0; i < count; i++)
            hash = unchecked(hash * 31 + steps[i].NpcId * 397 + steps[i].TileX * 17 + steps[i].TileY);
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
