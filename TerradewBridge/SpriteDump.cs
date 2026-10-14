using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terradew.Core;
using Terraria;
using Terraria.GameContent;

namespace TerradewBridge;

/// <summary>
/// Exports whole Terraria sprite sheets as PNG files for Stardew to draw from. Nothing is copied into
/// the project: the sheets come from the installed game at runtime. Each name is tried once per session.
/// </summary>
internal static class SpriteDump
{
    private static readonly HashSet<string> Done = new();

    public static void Npc(int type)
        => Sheet(SpriteNames.Npc(type), $"Images/NPC_{type}", () => { Main.instance.LoadNPC(type); return TextureAssets.Npc[type]; });

    public static void Projectile(int type)
        => Sheet(SpriteNames.Projectile(type), $"Images/Projectile_{type}", () => { Main.instance.LoadProjectile(type); return TextureAssets.Projectile[type]; });

    public static void Item(int type)
    {
        if (type <= 0)
            return;
        Sheet(SpriteNames.Item(type), $"Images/Item_{type}", () => { Main.instance.LoadItem(type); return TextureAssets.Item[type]; });
    }

    public static void Buff(int type)
    {
        if (type <= 0)
            return;
        Sheet(SpriteNames.Buff(type), $"Images/Buff_{type}", () => TextureAssets.Buff[type]);
    }

    public static void Head(int slot)
    {
        if (slot > 0)
            Frame(SpriteNames.Armor("head", slot), $"Images/Armor_Head_{slot}", () => { Main.instance.LoadArmorHead(slot); return TextureAssets.ArmorHead[slot]; });
    }

    public static void Body(int slot, bool male)
    {
        if (slot <= 0)
            return;
        string path = male ? $"Images/Armor/Armor_{slot}" : $"Images/Armor/Armor_Female_{slot}";
        Frame(SpriteNames.Armor("body", slot, male ? 0 : 1), path, () => { Main.instance.LoadArmorBody(slot); return male ? TextureAssets.ArmorBody[slot] : TextureAssets.FemaleBody[slot]; });
    }

    public static void Legs(int slot)
    {
        if (slot > 0)
            Frame(SpriteNames.Armor("legs", slot), $"Images/Armor_Legs_{slot}", () => { Main.instance.LoadArmorLegs(slot); return TextureAssets.ArmorLeg[slot]; });
    }

    private static void Sheet(string file, string assetPath, Func<Asset<Texture2D>> fallback)
    {
        if (!Done.Add(file) || SpriteFiles.Exists(file))
            return;
        Texture2D? texture = Load(assetPath, fallback);
        if (texture == null)
            return;
        Export(texture, new Rectangle(0, 0, texture.Width, texture.Height), file);
    }

    /// <summary>Armor sheets hold many frames; the first 40 by 56 frame is the standing pose.</summary>
    private static void Frame(string file, string assetPath, Func<Asset<Texture2D>> fallback)
    {
        if (!Done.Add(file) || SpriteFiles.Exists(file))
            return;
        Texture2D? texture = Load(assetPath, fallback);
        if (texture == null)
            return;
        Export(texture, new Rectangle(0, 0, Math.Min(40, texture.Width), Math.Min(56, texture.Height)), file);
    }

    private static Texture2D? Load(string assetPath, Func<Asset<Texture2D>> fallback)
    {
        try
        {
            Texture2D texture = Main.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad).Value;
            if (texture != null && texture.Width > 1 && texture.Height > 1)
                return texture;
        }
        catch (Exception)
        {
        }
        try
        {
            Asset<Texture2D> asset = fallback();
            Texture2D texture = asset.Value;
            return texture != null && texture.Width > 1 && texture.Height > 1 ? texture : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Export(Texture2D texture, Rectangle frame, string fileName)
    {
        try
        {
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
        catch (Exception error)
        {
            BridgeSystem.Instance.Mod.Logger.Debug($"Terradew could not export {fileName}: {error.Message}");
        }
    }
}
