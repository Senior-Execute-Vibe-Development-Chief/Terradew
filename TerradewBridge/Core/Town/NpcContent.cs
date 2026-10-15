using System;
using System.Collections.Generic;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Everything written for one Terraria town NPC living as a Stardew villager: their lines, gift tastes and
    /// heart events. One JSON file per NPC in Terradew/assets/npcs. Property names are read case-insensitively.
    /// </summary>
    public sealed class NpcContent
    {
        /// <summary>Terraria internal NPC name, like "Merchant". Must match TownHomes.</summary>
        public string Npc { get; set; } = "";

        /// <summary>The Stardew villager whose life this NPC takes over, like "Pierre". Must match TownHomes.</summary>
        public string Villager { get; set; } = "";

        /// <summary>Male, Female or Undefined. Used for pronouns.</summary>
        public string Gender { get; set; } = "Undefined";

        /// <summary>True for the Angler: Stardew treats them as a child.</summary>
        public bool Child { get; set; }

        /// <summary>Can be dated and married.</summary>
        public bool Romance { get; set; }

        /// <summary>Stardew dialogue keys (Characters/Dialogue/&lt;villager&gt;) to lines. Replaces the villager's own lines.</summary>
        public Dictionary<string, string> Dialogue { get; set; } = new();

        /// <summary>A line for rainy days (Characters/Dialogue/rainy).</summary>
        public string Rainy { get; set; } = "";

        /// <summary>Marriage dialogue keys (Characters/Dialogue/MarriageDialogue&lt;villager&gt;) to lines. Romance only.</summary>
        public Dictionary<string, string> Marriage { get; set; } = new();

        /// <summary>Lines about what happened in the Terraria world, by <see cref="Happenings"/> id.</summary>
        public Dictionary<string, string> Situations { get; set; } = new();

        /// <summary>Festival lines by festival id ("spring13"), plus "&lt;id&gt;_spouse" for a romance NPC.</summary>
        public Dictionary<string, string> Festivals { get; set; } = new();

        public GiftTable Gifts { get; set; } = new();

        public List<NpcEvent> Events { get; set; } = new();
    }

    public sealed class GiftTable
    {
        public GiftTaste Loved { get; set; } = new();
        public GiftTaste Liked { get; set; } = new();
        public GiftTaste Neutral { get; set; } = new();
        public GiftTaste Disliked { get; set; } = new();
        public GiftTaste Hated { get; set; } = new();
    }

    public sealed class GiftTaste
    {
        /// <summary>What they say when given something of this taste.</summary>
        public string Text { get; set; } = "";

        /// <summary>Stardew item ids ("72") or negative category ids ("-4").</summary>
        public List<string> Stardew { get; set; } = new();

        /// <summary>Terraria internal item names ("Dynamite") or tags ("#explosive", see <see cref="GiftTags"/>).</summary>
        public List<string> Terraria { get; set; } = new();
    }

    /// <summary>A heart event, written as simple steps and compiled to a Stardew event script by <see cref="EventCompiler"/>.</summary>
    public sealed class NpcEvent
    {
        /// <summary>Short id, unique for this NPC, like "h2".</summary>
        public string Id { get; set; } = "";

        /// <summary>2, 4, 6, 8 or 10. 14 for a romance NPC once married.</summary>
        public int Hearts { get; set; }

        /// <summary>"home" (the villager's home), or one of <see cref="EventCompiler.Places"/>.</summary>
        public string Where { get; set; } = "home";

        /// <summary>"day", "night" or "any".</summary>
        public string Time { get; set; } = "any";

        /// <summary>"sunny", "rainy" or "any".</summary>
        public string Weather { get; set; } = "any";

        /// <summary>Other Terraria NPCs (internal names) in the scene. The event waits until they all live in town.</summary>
        public List<string> With { get; set; } = new();

        /// <summary>One of <see cref="EventCompiler.Music"/>.</summary>
        public string Music { get; set; } = "none";

        public List<EventStep> Steps { get; set; } = new();

        /// <summary>What the NPC says if talked to for the rest of the day. Optional.</summary>
        public string After { get; set; } = "";

        /// <summary>A Terraria item (internal name) the farmer receives once the event is seen. Optional.</summary>
        public string Reward { get; set; } = "";

        public int RewardCount { get; set; } = 1;
    }

    /// <summary>
    /// One step. Exactly one of Say, Emote, Move, Face, Pause, Jump, Sound or Question is set. Who and Move name an
    /// actor: "self", "farmer", or an NPC from the event's With list.
    /// </summary>
    public sealed class EventStep
    {
        public string? Say { get; set; }
        public string? Who { get; set; }
        public string? Emote { get; set; }
        public string? Move { get; set; }
        public int Dx { get; set; }
        public int Dy { get; set; }
        public string? Face { get; set; }

        /// <summary>For Face: "up", "down", "left", "right", or an actor to turn toward.</summary>
        public string? Toward { get; set; }

        public int Pause { get; set; }
        public string? Jump { get; set; }
        public string? Sound { get; set; }
        public string? Question { get; set; }
        public List<EventAnswer>? Answers { get; set; }
    }

    public sealed class EventAnswer
    {
        public string Text { get; set; } = "";
        public int Friendship { get; set; }
        public string Reply { get; set; } = "";
    }
}
