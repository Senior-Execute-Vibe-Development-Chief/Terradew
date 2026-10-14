using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using Terradew.Core;

namespace Terradew;

internal sealed class SpriteBook
{
    private readonly Dictionary<string, Texture2D> loaded = new();

    public Texture2D? Load(string fileName)
    {
        if (loaded.TryGetValue(fileName, out Texture2D? cached))
            return cached;
        string path = Path.Combine(SpriteNames.Folder, fileName);
        if (!File.Exists(path))
            return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            Texture2D texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
            loaded[fileName] = texture;
            return texture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Texture2D? BackView(string fileName)
    {
        string key = "back:" + fileName;
        if (loaded.TryGetValue(key, out Texture2D? cached))
            return cached;
        Texture2D? source = Load(fileName);
        if (source == null)
            return null;
        var colors = new Color[source.Width * source.Height];
        source.GetData(colors);
        var rgba = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            rgba[i * 4] = colors[i].R;
            rgba[i * 4 + 1] = colors[i].G;
            rgba[i * 4 + 2] = colors[i].B;
            rgba[i * 4 + 3] = colors[i].A;
        }
        SpriteRecolor.ToBackView(rgba);
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Color(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        var copy = new Texture2D(Game1.graphics.GraphicsDevice, source.Width, source.Height);
        copy.SetData(colors);
        loaded[key] = copy;
        return copy;
    }
}
