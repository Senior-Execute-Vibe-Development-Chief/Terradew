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
/// the project: the sheets come from the installed game at runtime. Each type is requested once per
/// session; a failed export is retried a few seconds later, up to <see cref="MaxAttempts"/> tries in all.
/// </summary>
internal static class SpriteDump
{
    /// <summary>Tries per sheet before giving up for the session.</summary>
    private const int MaxAttempts = 3;

    private static readonly HashSet<int> npcDone = new();
    private static readonly HashSet<int> projectileDone = new();
    private static readonly HashSet<int> itemDone = new();
    private static readonly HashSet<int> buffDone = new();
    private static readonly HashSet<int> headDone = new();
    private static readonly HashSet<int> bodyDone = new();
    private static readonly HashSet<int> legsDone = new();
    private static readonly HashSet<int> wingsDone = new();
    private static readonly HashSet<int> shieldDone = new();
    private static readonly HashSet<int> backDone = new();
    private static readonly HashSet<int> frontDone = new();
    private static readonly HashSet<int> shoeDone = new();

    /// <summary>Ticks to wait before trying a failed sheet again, so assets still loading get time to finish.</summary>
    private const uint RetryDelay = 300;

    /// <summary>Failed tries so far, by file name.</summary>
    private static readonly Dictionary<string, int> failures = new();

    /// <summary>Failed sheets waiting for their retry tick: the done set to clear, the key, and when.</summary>
    private static uint ticks;

    private static readonly List<(HashSet<int> Done, int Key, uint At)> retries = new();

    // The public entry points check the done set before anything else. Names, asset paths and the
    // loader closures are built only in the Export helpers below, which run once per type.

    public static void Npc(int type)
    {
        if (!npcDone.Add(type))
            return;
        ExportNpc(type);
    }

    public static void Projectile(int type)
    {
        if (!projectileDone.Add(type))
            return;
        ExportProjectile(type);
    }

    public static void Item(int type)
    {
        if (type <= 0 || !itemDone.Add(type))
            return;
        ExportItem(type);
    }

    public static void Buff(int type)
    {
        if (type <= 0 || !buffDone.Add(type))
            return;
        ExportBuff(type);
    }

    public static void Head(int slot)
    {
        if (slot <= 0 || !headDone.Add(slot))
            return;
        ExportHead(slot);
    }

    public static void Body(int slot, bool male)
    {
        if (slot <= 0 || !bodyDone.Add(BodyKey(slot, male)))
            return;
        ExportBody(slot, male);
    }

    public static void Legs(int slot)
    {
        if (slot <= 0 || !legsDone.Add(slot))
            return;
        ExportLegs(slot);
    }

    public static void Wings(int slot)
    {
        if (slot <= 0 || !wingsDone.Add(slot))
            return;
        ExportWings(slot);
    }

    public static void Shield(int slot)
    {
        if (slot <= 0 || !shieldDone.Add(slot))
            return;
        ExportShield(slot);
    }

    public static void Back(int slot)
    {
        if (slot <= 0 || !backDone.Add(slot))
            return;
        ExportBack(slot);
    }

    public static void Front(int slot)
    {
        if (slot <= 0 || !frontDone.Add(slot))
            return;
        ExportFront(slot);
    }

    public static void Shoe(int slot)
    {
        if (slot <= 0 || !shoeDone.Add(slot))
            return;
        ExportShoe(slot);
    }

    private static void ExportNpc(int type)
    {
        string file = SpriteNames.Npc(type);
        string? error = Sheet(file, $"Images/NPC_{type}", () => { Main.instance.LoadNPC(type); return TextureAssets.Npc[type]; });
        Failed(npcDone, type, file, error);
    }

    private static void ExportProjectile(int type)
    {
        string file = SpriteNames.Projectile(type);
        string? error = Sheet(file, $"Images/Projectile_{type}", () => { Main.instance.LoadProjectile(type); return TextureAssets.Projectile[type]; });
        Failed(projectileDone, type, file, error);
    }

    private static void ExportItem(int type)
    {
        string file = SpriteNames.Item(type);
        string? error = Sheet(file, $"Images/Item_{type}", () => { Main.instance.LoadItem(type); return TextureAssets.Item[type]; });
        Failed(itemDone, type, file, error);
    }

    private static void ExportBuff(int type)
    {
        string file = SpriteNames.Buff(type);
        string? error = Sheet(file, $"Images/Buff_{type}", () => TextureAssets.Buff[type]);
        Failed(buffDone, type, file, error);
    }

    private static void ExportHead(int slot)
    {
        string file = SpriteNames.Armor("head", slot);
        string? error = Frame(file, $"Images/Armor_Head_{slot}", () => { Main.instance.LoadArmorHead(slot); return TextureAssets.ArmorHead[slot]; });
        Failed(headDone, slot, file, error);
    }

    private static void ExportBody(int slot, bool male)
    {
        string file = SpriteNames.Armor("body", slot, male ? 0 : 1);
        string path = male ? $"Images/Armor/Armor_{slot}" : $"Images/Armor/Armor_Female_{slot}";
        string? error = Frame(file, path, () => { Main.instance.LoadArmorBody(slot); return male ? TextureAssets.ArmorBody[slot] : TextureAssets.FemaleBody[slot]; });
        Failed(bodyDone, BodyKey(slot, male), file, error);
    }

    private static void ExportLegs(int slot)
    {
        string file = SpriteNames.Armor("legs", slot);
        string? error = Frame(file, $"Images/Armor_Legs_{slot}", () => { Main.instance.LoadArmorLegs(slot); return TextureAssets.ArmorLeg[slot]; });
        Failed(legsDone, slot, file, error);
    }

    private static void ExportWings(int slot)
    {
        string file = SpriteNames.Accessory("wings", slot);
        string? error = Frame(file, $"Images/Wings_{slot}", () => { Main.instance.LoadWings(slot); return TextureAssets.Wings[slot]; }, WingsFrame);
        Failed(wingsDone, slot, file, error);
    }

    private static void ExportShield(int slot)
    {
        string file = SpriteNames.Accessory("shield", slot);
        string? error = Frame(file, $"Images/Acc_Shield_{slot}", () => { Main.instance.LoadAccShield(slot); return TextureAssets.AccShield[slot]; });
        Failed(shieldDone, slot, file, error);
    }

    private static void ExportBack(int slot)
    {
        string file = SpriteNames.Accessory("back", slot);
        string? error = Frame(file, $"Images/Acc_Back_{slot}", () => { Main.instance.LoadAccBack(slot); return TextureAssets.AccBack[slot]; });
        Failed(backDone, slot, file, error);
    }

    private static void ExportFront(int slot)
    {
        string file = SpriteNames.Accessory("front", slot);
        string? error = Frame(file, $"Images/Acc_Front_{slot}", () => { Main.instance.LoadAccFront(slot); return TextureAssets.AccFront[slot]; });
        Failed(frontDone, slot, file, error);
    }

    private static void ExportShoe(int slot)
    {
        string file = SpriteNames.Accessory("shoe", slot);
        string? error = Frame(file, $"Images/Acc_Shoes_{slot}", () => { Main.instance.LoadAccShoes(slot); return TextureAssets.AccShoes[slot]; });
        Failed(shoeDone, slot, file, error);
    }

    /// <summary>Lets failed sheets whose wait is over be requested again. Call once per bridge tick.</summary>
    public static void Tick()
    {
        ticks++;
        for (int i = retries.Count - 1; i >= 0; i--)
        {
            if (ticks < retries[i].At)
                continue;
            retries[i].Done.Remove(retries[i].Key);
            retries.RemoveAt(i);
        }
    }

    private static int BodyKey(int slot, bool male) => slot * 2 + (male ? 0 : 1);

    /// <summary>Exports a whole sheet. Returns null when the file is written or already there, else the reason it failed.</summary>
    private static string? Sheet(string file, string assetPath, Func<Asset<Texture2D>> fallback)
    {
        if (SpriteFiles.Exists(file))
            return null;
        Texture2D? texture = Load(assetPath, fallback);
        if (texture == null)
            return $"no texture at {assetPath}";
        return Export(texture, new Rectangle(0, 0, texture.Width, texture.Height), file);
    }

    /// <summary>
    /// Armor sheets hold many frames; the first 40 by 56 frame is the standing pose. <paramref name="pick"/> chooses
    /// another frame when a sheet's size is not 40 by 56, such as wings.
    /// </summary>
    private static string? Frame(string file, string assetPath, Func<Asset<Texture2D>> fallback, Func<Texture2D, Rectangle>? pick = null)
    {
        if (SpriteFiles.Exists(file))
            return null;
        Texture2D? texture = Load(assetPath, fallback);
        if (texture == null)
            return $"no texture at {assetPath}";
        Rectangle frame = pick?.Invoke(texture) ?? new Rectangle(0, 0, Math.Min(40, texture.Width), Math.Min(56, texture.Height));
        return Export(texture, frame, file);
    }

    /// <summary>Wings sheets stack their frames vertically, four to a sheet: the first frame is the full width and a quarter of the height.</summary>
    private static Rectangle WingsFrame(Texture2D texture) => new(0, 0, texture.Width, Math.Max(1, texture.Height / 4));

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

    /// <summary>Returns null when written, else the exception message.</summary>
    private static string? Export(Texture2D texture, Rectangle frame, string fileName)
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
            return null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }

    /// <summary>
    /// Queues a failed type to leave its done set after <see cref="RetryDelay"/> ticks, so it is tried again. After <see cref="MaxAttempts"/>
    /// failures it stays done and one Warn is logged.
    /// </summary>
    private static void Failed(HashSet<int> done, int key, string file, string? error)
    {
        if (error == null)
            return;
        int attempts = failures.TryGetValue(file, out int count) ? count + 1 : 1;
        failures[file] = attempts;
        if (attempts < MaxAttempts)
        {
            retries.Add((done, key, ticks + RetryDelay));
            return;
        }
        BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not export {file} after {attempts} tries, so it will not be retried: {error}");
    }
}
