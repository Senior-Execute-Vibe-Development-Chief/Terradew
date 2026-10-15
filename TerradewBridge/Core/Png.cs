using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Terradew.Core;

public static class Png
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("RGBA buffer is shorter than the image.", nameof(rgba));

        var raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (1 + width * 4);
            raw[row] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, row + 1, width * 4);
        }

        using var stream = File.Create(path);
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(stream, "IHDR", Header(width, height));
        WriteChunk(stream, "IDAT", Zlib(raw));
        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    private static byte[] Zlib(byte[] raw)
    {
        using var payload = new MemoryStream();
        using (var deflate = new DeflateStream(payload, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);
        byte[] deflated = payload.ToArray();
        uint s1 = 1;
        uint s2 = 0;
        foreach (byte value in raw)
        {
            s1 = (s1 + value) % 65521;
            s2 = (s2 + s1) % 65521;
        }
        uint adler = (s2 << 16) | s1;
        var zlib = new byte[2 + deflated.Length + 4];
        zlib[0] = 0x78;
        zlib[1] = 0x9C;
        Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
        WriteInt(zlib, zlib.Length - 4, (int)adler);
        return zlib;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var tag = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);
        stream.Write(tag);
        stream.Write(data);
        var crcSource = new byte[tag.Length + data.Length];
        Buffer.BlockCopy(tag, 0, crcSource, 0, tag.Length);
        Buffer.BlockCopy(data, 0, crcSource, tag.Length, data.Length);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(crcSource));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
