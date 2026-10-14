using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terradew.Core;

namespace TerradewBridge;

internal static class SpriteDump
{
    public static void Npc(NPC npc)
    {
        Rectangle frame = npc.frame;
        if (frame.Width <= 0 || frame.Height <= 0)
            return;
        if (SpriteFiles.Exists(SpriteNames.Npc(npc.type, frame.X, frame.Y, frame.Width, frame.Height)))
            return;
        try
        {
            Main.instance.LoadNPC(npc.type);
            Texture2D texture = TextureAssets.Npc[npc.type].Value;
            Rectangle sample = Fit(texture, frame);
            if (sample.Width <= 0 || sample.Height <= 0)
                return;
            Export(texture, sample, SpriteNames.Npc(npc.type, frame.X, frame.Y, frame.Width, frame.Height));
        }
        catch (Exception)
        {
            // A missing sheet must not stop the NPC snapshot.
        }
    }

    public static void Item(int type)
    {
        if (type <= 0 || SpriteFiles.Exists(SpriteNames.Item(type)))
            return;
        try
        {
            Main.instance.LoadItem(type);
            Texture2D texture = TextureAssets.Item[type].Value;
            Export(texture, texture.Bounds, SpriteNames.Item(type));
        }
        catch (Exception)
        {
        }
    }

    public static void Head(int slot)
        => Armor("head", slot, 0, Main.instance.LoadArmorHead, TextureAssets.ArmorHead);

    public static void Body(int slot, bool male)
        => Armor("body", slot, male ? 0 : 1, Main.instance.LoadArmorBody, male ? TextureAssets.ArmorBody : TextureAssets.FemaleBody);

    public static void Legs(int slot)
        => Armor("legs", slot, 0, Main.instance.LoadArmorLegs, TextureAssets.ArmorLeg);

    private static void Armor(string part, int slot, int variant, Action<int> load, Asset<Texture2D>[] sheet)
    {
        if (slot <= 0 || slot >= sheet.Length || SpriteFiles.Exists(SpriteNames.Armor(part, slot, variant)))
            return;
        try
        {
            load(slot);
            Texture2D texture = sheet[slot].Value;
            int width = Math.Min(40, texture.Width);
            int height = Math.Min(56, texture.Height);
            if (width <= 0 || height <= 0)
                return;
            Export(texture, new Rectangle(0, 0, width, height), SpriteNames.Armor(part, slot, variant));
        }
        catch (Exception)
        {
        }
    }

    private static void Export(Texture2D texture, Rectangle frame, string fileName)
    {
        if (SpriteFiles.Exists(fileName))
            return;
        var colors = new Color[frame.Width * frame.Height];
        texture.GetData(0, frame, colors, 0, colors.Length);
        var rgba = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            rgba[i * 4] = colors[i].R;
            rgba[i * 4 + 1] = colors[i].G;
            rgba[i * 4 + 2] = colors[i].B;
            rgba[i * 4 + 3] = colors[i].A;
        }
        SpriteFiles.Publish(fileName, frame.Width, frame.Height, rgba);
    }

    private static Rectangle Fit(Texture2D texture, Rectangle frame)
    {
        int x = Math.Clamp(frame.X, 0, Math.Max(0, texture.Width - 1));
        int y = Math.Clamp(frame.Y, 0, Math.Max(0, texture.Height - 1));
        int width = Math.Min(frame.Width, texture.Width - x);
        int height = Math.Min(frame.Height, texture.Height - y);
        return new Rectangle(x, y, width, height);
    }
}
