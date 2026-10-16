using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terradew.Core;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

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
    private static readonly HashSet<int> npcHeadDone = new();
    private static readonly HashSet<int> npcPortraitDone = new();
    private static readonly HashSet<int> lookDone = new();
    private static readonly Dictionary<Asset<Texture2D>, string> lookNames = new();
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
    private static readonly HashSet<int> potDone = new();
    private static readonly HashSet<int> dustDone = new();

    /// <summary>Pot styles run from 0 to this, the last style of the Pots tile sheet.</summary>
    private const int LastPotStyle = 36;

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

    public static void NpcHead(int headIndex)
    {
        if (headIndex < 0 || !npcHeadDone.Add(headIndex))
            return;
        ExportNpcHead(headIndex);
    }

    /// <summary>
    /// Exports the sheet a town NPC is drawn with right now and returns its file name, or "" for an NPC without a
    /// town profile. Terraria swaps a town NPC's texture by its profile (party hat, shimmer, a pet's breed), and the
    /// plain NPC_ sheet is not always the art the game shows: the Princess's is not hers.
    /// </summary>
    public static string Look(NPC npc)
    {
        if (!TownNPCProfiles.Instance.GetProfile(npc, out ITownNPCProfile profile))
            return "";
        Asset<Texture2D>? asset = profile.GetTextureNPCShouldUse(npc);
        if (asset == null || string.IsNullOrEmpty(asset.Name))
            return "";
        if (!lookNames.TryGetValue(asset, out string? file))
        {
            file = SpriteNames.Look(asset.Name);
            lookNames[asset] = file;
        }
        int key = file.GetHashCode();
        if (lookDone.Add(key))
        {
            string? error = Sheet(file, asset.Name, () => { asset.Wait?.Invoke(); return asset; });
            Failed(lookDone, key, file, error);
        }
        return file;
    }

    /// <summary>
    /// Exports a town NPC's detailed portrait. Only Terraria 1.4.5 and later has them, so a missing one is not an
    /// error: it is not retried, and Stardew cuts a portrait from the NPC's sprite instead.
    /// </summary>
    public static void NpcPortrait(int type)
    {
        if (!npcPortraitDone.Add(type))
            return;
        ExportNpcPortrait(type);
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

    /// <summary>Exports one Terraria pot style, a 2 by 2 tile pot from the Pots sheet.</summary>
    public static void Pot(int style)
    {
        if (style < 0 || !potDone.Add(style))
            return;
        ExportPot(style);
    }

    /// <summary>Exports Terraria's dust sheet, once. Cheap after the first call.</summary>
    public static void DustSheet()
    {
        if (!dustDone.Add(0))
            return;
        ExportDust();
    }

    /// <summary>Exports every Terraria pot style, once each. Cheap after the first call.</summary>
    public static void Pots()
    {
        for (int style = 0; style <= LastPotStyle; style++)
            Pot(style);
    }

    private static void ExportNpc(int type)
    {
        string file = SpriteNames.Npc(type);
        string? error = Sheet(file, $"Images/NPC_{type}", () => { Main.instance.LoadNPC(type); return TextureAssets.Npc[type]; });
        Failed(npcDone, type, file, error);
    }

    private static void ExportNpcHead(int headIndex)
    {
        string file = SpriteNames.NpcHead(headIndex);
        string? error = Sheet(file, $"Images/NPC_Head_{headIndex}", () => TextureAssets.NpcHead[headIndex]);
        Failed(npcHeadDone, headIndex, file, error);
    }

    private static void ExportNpcPortrait(int type)
    {
        string file = SpriteNames.NpcPortrait(type);
        if (SpriteFiles.Exists(file))
            return;
        // Terraria's own table says which art each town NPC's chat portrait is, so ask it first. Its file names are
        // not always the internal name (Portrait_Bunny_Angora), and a NPC like the Tavernkeep or the Zoologist may
        // not follow the pattern. Shimmer and transformed variants are the table's other entries; the default is shown.
        if (ExportFromPortraitTable(type, file))
            return;
        string name = NPCID.Search.GetName(type);
        // Vanilla portraits are named after the NPC's internal name. Modded names hold a slash and have none here.
        if (string.IsNullOrEmpty(name) || name.Contains('/'))
            return;
        string path = $"Images/TownNPCs/Portraits/Portrait_{name}";
        Texture2D? texture = null;
        try
        {
            // Asking for a missing asset makes tModLoader show an error, even when the failure is caught.
            if (VanillaHas(path))
                texture = Main.Assets.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
        }
        catch (Exception)
        {
        }
        if (texture == null || texture.Width <= 1 || texture.Height <= 1)
        {
            if (!portraitsMissingLogged)
            {
                portraitsMissingLogged = true;
                BridgeSystem.Instance.Mod.Logger.Info($"Terradew found no detailed portrait at {path}, so Stardew portraits are cut from the Terraria sprites. Detailed portraits come with Terraria 1.4.5.");
            }
            return;
        }
        string? error = Export(texture, new Rectangle(0, 0, texture.Width, texture.Height), file);
        if (error != null)
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not export {file}: {error}");
    }

    /// <summary>
    /// Exports the portrait Terraria's NPCPortraits table draws for a town NPC, cropped to the frame the provider picks.
    /// Returns false when the table has no texture for the NPC (none, or not loaded yet), so the caller can try a file name.
    /// </summary>
    private static bool ExportFromPortraitTable(int type, string file)
    {
        try
        {
            if (!NPCID.Sets.NPCPortraits.TryGetValue(type, out NPCID.Sets.NPCPortraitProvider? provider) || provider == null)
                return false;
            provider.GetDrawData(out Texture2D? texture, out Rectangle frame);
            if (texture == null || texture.Width <= 1 || texture.Height <= 1)
                return false;
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Right > texture.Width || frame.Bottom > texture.Height)
                frame = new Rectangle(0, 0, texture.Width, texture.Height);
            string? error = Export(texture, frame, file);
            if (error == null)
                return true;
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not export {file} from Terraria's portrait table: {error}");
            return false;
        }
        catch (Exception error)
        {
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not read Terraria's portrait table for NPC {type}: {error.Message}");
            return false;
        }
    }

    private static bool portraitsMissingLogged;

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

    private static void ExportDust()
    {
        string file = SpriteNames.Dust;
        string? error = Sheet(file, "Images/Dust", () => TextureAssets.Dust);
        Failed(dustDone, 0, file, error);
    }

    private static void ExportPot(int style)
    {
        string file = SpriteNames.Pot(style);
        if (SpriteFiles.Exists(file))
            return;
        Texture2D? texture = Load($"Images/Tiles_{TileID.Pots}", () => { Main.instance.LoadTiles(TileID.Pots); return TextureAssets.Tile[TileID.Pots]; });
        string? error = texture == null ? $"no texture at Images/Tiles_{TileID.Pots}" : ExportPotCells(texture, style, file);
        Failed(potDone, style, file, error);
    }

    /// <summary>
    /// Joins a pot's four 16 by 16 cells into one 32 by 32 sprite. The sheet puts a 2 pixel gap between the cells of a
    /// 2 by 2 tile, and a frame that spans the gap would show the four parts apart, so each cell is copied on its own.
    /// </summary>
    private static string? ExportPotCells(Texture2D texture, int style, string file)
    {
        try
        {
            const int cell = 16;
            const int pitch = 18;
            const int size = cell * 2;
            var rgba = new byte[size * size * 4];
            var colors = new Color[cell * cell];
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    var source = new Rectangle(column * pitch, style * pitch * 2 + row * pitch, cell, cell);
                    texture.GetData(0, source, colors, 0, colors.Length);
                    for (int y = 0; y < cell; y++)
                    {
                        for (int x = 0; x < cell; x++)
                        {
                            Color color = colors[y * cell + x];
                            int at = (((row * cell + y) * size) + column * cell + x) * 4;
                            rgba[at] = color.R;
                            rgba[at + 1] = color.G;
                            rgba[at + 2] = color.B;
                            rgba[at + 3] = color.A;
                        }
                    }
                }
            }
            SpriteFiles.Publish(file, size, size, rgba);
            return null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
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
            if (VanillaHas(assetPath))
            {
                Texture2D texture = Main.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad).Value;
                if (texture != null && texture.Width > 1 && texture.Height > 1)
                    return texture;
            }
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

    /// <summary>
    /// Whether Terraria's own content holds this asset. Requesting one that is missing (a modded path, or art from a
    /// newer Terraria than tModLoader runs) pops up a tModLoader error even when the exception is caught.
    /// </summary>
    private static bool VanillaHas(string assetPath)
    {
        try
        {
            return ModContent.HasAsset("Terraria/" + assetPath);
        }
        catch (Exception)
        {
            return false;
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
