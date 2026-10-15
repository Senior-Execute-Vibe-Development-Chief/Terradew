using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

namespace Terradew;

/// <summary>
/// Who plays whom. Each villager in <see cref="TownHomes"/> is played by a Terraria town NPC once that NPC has moved
/// into the villager's home: the villager keeps their schedule and friendship, but looks like the Terraria NPC, goes
/// by its name, and speaks the lines in assets/npcs. Villagers nobody plays stay out of sight.
/// </summary>
internal sealed class Cast
{
    /// <summary>Regular villagers with no Terraria NPC. They are hidden, and leave the social page.</summary>
    public static readonly IReadOnlySet<string> Unplayed = new HashSet<string>(StringComparer.Ordinal)
    {
        "Abigail", "Caroline", "George", "Jas", "Jodi", "Maru", "Penny", "Sam", "Shane", "Vincent",
    };

    /// <summary>Villager name to the resident who plays them.</summary>
    private static Dictionary<string, TownResident> playing = new(StringComparer.Ordinal);
    private string signature = "";

    private readonly IModHelper helper;
    private readonly SpriteBook sprites;
    private readonly IMonitor monitor;
    private readonly Func<string, NpcContent?> content;

    public Cast(IModHelper helper, SpriteBook sprites, IMonitor monitor, Func<string, NpcContent?> content)
    {
        this.helper = helper;
        this.sprites = sprites;
        this.monitor = monitor;
        this.content = content;
    }

    /// <summary>The resident playing a villager, when someone has moved into their home.</summary>
    public static TownResident? Playing(string? villager)
        => villager != null && playing.TryGetValue(villager, out TownResident resident) ? resident : null;

    /// <summary>A Terraria town NPC type that is on stage as a villager, so its own Terraria body is not drawn.</summary>
    public static bool Plays(int npcType) => playing.Values.Any(r => r.NpcType == npcType);

    /// <summary>Whether the farmer sees (and can talk to) this NPC.</summary>
    public static bool Shown(NPC npc)
    {
        if (!npc.IsVillager)
            return true;
        if (TownHomes.NpcOf(npc.Name) != null)
            return playing.ContainsKey(npc.Name);
        return !Unplayed.Contains(npc.Name);
    }

    public void Reset()
    {
        playing = new(StringComparer.Ordinal);
        signature = "";
    }

    /// <summary>Picks up who lives where from Terraria. When it changes, the villagers' looks are rebuilt.</summary>
    public void Update(WorldFacts? facts)
    {
        if (facts == null)
            return;
        var next = new Dictionary<string, TownResident>(StringComparer.Ordinal);
        foreach (TownResident resident in facts.Residents)
        {
            if (string.IsNullOrEmpty(resident.HomePlaceKey))
                continue;
            string? npc = TownHomes.Npcs.FirstOrDefault(n => TownHomes.TypeOf(n) == resident.NpcType);
            string? villager = npc == null ? null : TownHomes.VillagerOf(npc);
            if (villager != null)
                next[villager] = resident;
        }
        // The sprite sheet may land on disk after the facts, so whether it is there is part of what changed.
        string now = string.Join(";", next.OrderBy(p => p.Key).Select(p =>
            $"{p.Key}={p.Value.NpcType},{p.Value.Name},{p.Value.HeadIndex},{p.Value.FrameCount},{sprites.Load(SpriteNames.Npc(p.Value.NpcType)) != null},{sprites.Load(SpriteNames.NpcHead(p.Value.HeadIndex)) != null}"));
        if (now == signature)
            return;
        signature = now;
        playing = next;
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            helper.GameContent.InvalidateCache("Data/Characters");
            foreach (string npc in TownHomes.Npcs)
            {
                string? villager = TownHomes.VillagerOf(npc);
                if (villager == null)
                    continue;
                helper.GameContent.InvalidateCache($"Characters/{villager}");
                helper.GameContent.InvalidateCache($"Portraits/{villager}");
            }
            if (!Context.IsWorldReady)
                return;
            Utility.ForEachVillager(npc =>
            {
                if (TownHomes.NpcOf(npc.Name) != null)
                    npc.reloadSprite();
                npc.farmerPassesThrough = !Shown(npc);
                return true;
            });
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not update how villagers look. {error.Message}", LogLevel.Warn);
        }
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Characters"))
        {
            e.Edit(asset => EditCharacters(asset.AsDictionary<string, CharacterData>().Data), AssetEditPriority.Late);
            return;
        }
        foreach (var pair in playing)
        {
            string villager = pair.Key;
            TownResident resident = pair.Value;
            if (e.NameWithoutLocale.IsEquivalentTo($"Characters/{villager}"))
            {
                Texture2D? sheet = sprites.Load(SpriteNames.Npc(resident.NpcType));
                if (sheet != null)
                    e.LoadFrom(() => Walker(sheet, resident), AssetLoadPriority.High);
                return;
            }
            if (e.NameWithoutLocale.IsEquivalentTo($"Portraits/{villager}"))
            {
                Texture2D? head = resident.HeadIndex >= 0 ? sprites.Load(SpriteNames.NpcHead(resident.HeadIndex)) : null;
                if (head != null)
                    e.LoadFrom(() => Portrait(head), AssetLoadPriority.High);
                return;
            }
        }
    }

    private void EditCharacters(IDictionary<string, CharacterData> data)
    {
        try
        {
            foreach (var pair in data)
            {
                string name = pair.Key;
                CharacterData character = pair.Value;
                string? npc = TownHomes.NpcOf(name);
                if (npc == null)
                {
                    if (Unplayed.Contains(name))
                    {
                        character.CanBeRomanced = false;
                        character.CanSocialize = "FALSE";
                    }
                    continue;
                }
                // Kent and Leo arrive later in vanilla; their Terraria NPCs decide when they move in instead.
                character.UnlockConditions = null;
                NpcContent? lines = content(npc);
                character.CanBeRomanced = ContentCheck.Romance.Contains(npc);
                if (Playing(name) is not TownResident resident)
                {
                    character.CanSocialize = "FALSE";
                    continue;
                }
                character.CanSocialize = null;
                character.DisplayName = resident.Name;
                if (lines != null)
                {
                    character.Gender = lines.Gender switch
                    {
                        "Male" => Gender.Male,
                        "Female" => Gender.Female,
                        _ => Gender.Undefined,
                    };
                    character.Age = lines.Child ? NpcAge.Child : NpcAge.Adult;
                }
                // The Terraria sprite has no breathing chest and no seasonal outfits.
                character.Breather = false;
                character.Appearance?.Clear();
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not recast the villagers. {error.Message}", LogLevel.Warn);
        }
    }

    private const int FrameWidth = 16;
    private const int FrameHeight = 32;
    private const int Frames = 64;

    /// <summary>
    /// A Stardew walking sheet (four 16x32 frames per row: down, right, up, left) from a Terraria town NPC sheet:
    /// one tall column of frames drawn at double size, facing left. The rest of the 64 frames stand still.
    /// </summary>
    private static Texture2D Walker(Texture2D source, TownResident resident)
    {
        int count = Math.Max(1, resident.FrameCount);
        int frameHeight = source.Height / count;
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        // Frame 0 stands; walk frames start at 2 and stop before the extra (sitting, talking) frames.
        int walkFirst = Math.Min(2, count - 1);
        int walkLast = Math.Max(walkFirst, count - Math.Max(0, resident.ExtraFrames) - 1);
        int[] columns = new int[4];
        columns[0] = 0;
        for (int i = 1; i < 4; i++)
            columns[i] = walkFirst + (walkLast - walkFirst) * (i - 1) / 2;

        var sheet = new Color[FrameWidth * 4 * FrameHeight * (Frames / 4)];
        int sheetWidth = FrameWidth * 4;
        for (int frame = 0; frame < Frames; frame++)
        {
            int row = frame / 4;
            int column = frame % 4;
            // Rows past the four walking ones are copies of the standing frame facing down.
            int sourceFrame = row < 4 ? columns[column] : 0;
            bool flip = row is 0 or 1 || row >= 4;
            int destX = column * FrameWidth;
            int destY = row * FrameHeight;
            for (int y = 0; y < FrameHeight; y++)
            {
                // Halve the Terraria art and stand it on the bottom of the frame.
                int halfHeight = frameHeight / 2;
                int fromY = y - (FrameHeight - halfHeight);
                if (fromY < 0 || fromY >= halfHeight)
                    continue;
                for (int x = 0; x < FrameWidth; x++)
                {
                    int halfWidth = source.Width / 2;
                    int fromX = x + (halfWidth - FrameWidth) / 2;
                    if (flip)
                        fromX = halfWidth - 1 - fromX;
                    if (fromX < 0 || fromX >= halfWidth)
                        continue;
                    int sx = fromX * 2;
                    int sy = sourceFrame * frameHeight + fromY * 2;
                    if (sx >= source.Width || sy >= source.Height)
                        continue;
                    sheet[(destY + y) * sheetWidth + destX + x] = pixels[sy * source.Width + sx];
                }
            }
        }
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, sheetWidth, FrameHeight * (Frames / 4));
        texture.SetData(sheet);
        return texture;
    }

    /// <summary>A Stardew portrait sheet (64x64 faces, two across) with the Terraria head in every face.</summary>
    private static Texture2D Portrait(Texture2D head)
    {
        const int cell = 64;
        const int across = 2;
        const int down = 3;
        var pixels = new Color[head.Width * head.Height];
        head.GetData(pixels);
        int scale = Math.Max(1, Math.Min(cell / Math.Max(1, head.Width), cell / Math.Max(1, head.Height)));
        int offsetX = (cell - head.Width * scale) / 2;
        int offsetY = (cell - head.Height * scale) / 2;
        int width = cell * across;
        var sheet = new Color[width * cell * down];
        for (int face = 0; face < across * down; face++)
        {
            int cellX = face % across * cell;
            int cellY = face / across * cell;
            for (int y = 0; y < head.Height * scale; y++)
            {
                for (int x = 0; x < head.Width * scale; x++)
                {
                    int dx = cellX + offsetX + x;
                    int dy = cellY + offsetY + y;
                    if (dx < cellX || dy < cellY || dx >= cellX + cell || dy >= cellY + cell)
                        continue;
                    sheet[dy * width + dx] = pixels[y / scale * head.Width + x / scale];
                }
            }
        }
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, width, cell * down);
        texture.SetData(sheet);
        return texture;
    }
}
