using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>Test helpers, typed in Terraria chat or in the SMAPI console as "terradew &lt;command&gt;".</summary>
public static class Commands
{
    public const string Help = "summon [npc] | boss <npc> | give <item> [count] | wear <item> [item...] | bring [npc] | heal";

    public static string Run(Player player, string text)
    {
        string[] parts = (text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return Help;
        switch (parts[0].ToLowerInvariant())
        {
            case "summon":
            {
                int type = NPCID.GreenSlime;
                if (parts.Length > 1 && !NPCID.Search.TryGetId(parts[1], out type))
                    return $"No NPC called {parts[1]}.";
                int x = (int)player.Center.X + player.direction * 16 * 8;
                int who = NPC.NewNPC(new EntitySource_Misc("Terradew summon"), x, (int)(player.position.Y + player.height), type, Target: player.whoAmI);
                return who >= 0 && who < Main.maxNPCs ? $"Summoned {Main.npc[who].FullName}." : "Nothing came.";
            }
            case "boss":
            {
                if (parts.Length < 2 || !NPCID.Search.TryGetId(parts[1], out int type))
                    return "Usage: boss <npc>, for example boss EyeofCthulhu.";
                NPC.SpawnOnPlayer(player.whoAmI, type);
                return $"Spawned {parts[1]}.";
            }
            case "give":
            {
                if (parts.Length < 2 || !ItemID.Search.TryGetId(parts[1], out int type))
                    return "Usage: give <item> [count], for example give CopperShortsword.";
                int count = parts.Length > 2 && int.TryParse(parts[2], out int n) ? n : 1;
                Inventory.Give(player, type, count);
                Inventory.ForceResend();
                return $"Gave {count} {parts[1]}.";
            }
            case "wear":
            {
                if (parts.Length < 2)
                    return "Usage: wear <item> [item...], for example wear CopperHelmet CopperChainmail CopperGreaves.";
                var worn = new System.Collections.Generic.List<string>();
                foreach (string name in parts[1..])
                {
                    if (!ItemID.Search.TryGetId(name, out int type))
                        return $"No item called {name}.";
                    Inventory.Give(player, type, 1);
                    int slot = Array.FindIndex(player.inventory, 0, 50, i => i.type == type);
                    if (slot < 0)
                        return $"No room in the inventory for {name}.";
                    Inventory.Equip(player, slot);
                    worn.Add(name);
                }
                Inventory.ForceResend();
                return "Wearing " + string.Join(", ", worn) + ".";
            }
            case "bring":
            {
                int type = NPCID.Guide;
                if (parts.Length > 1 && !NPCID.Search.TryGetId(parts[1], out type))
                    return $"No NPC called {parts[1]}.";
                int x = (int)player.Center.X + player.direction * 16 * 3;
                int y = (int)(player.position.Y + player.height);
                int who = NPC.FindFirstNPC(type);
                if (who < 0)
                {
                    who = NPC.NewNPC(new EntitySource_Misc("Terradew bring"), x, y, type);
                    if (who < 0 || who >= Main.maxNPCs)
                        return "Nothing came.";
                    return $"The {Main.npc[who].FullName} came.";
                }
                NPC npc = Main.npc[who];
                npc.position = new Microsoft.Xna.Framework.Vector2(x - npc.width / 2f, y - npc.height);
                npc.velocity = Microsoft.Xna.Framework.Vector2.Zero;
                return $"Brought {npc.FullName}. Right-click to talk; they wander home later.";
            }
            case "heal":
                player.statLife = player.statLifeMax2;
                player.statMana = player.statManaMax2;
                return "Healed.";
            default:
                return Help;
        }
    }
}

public sealed class TerradewCommand : ModCommand
{
    public override string Command => "terradew";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/terradew " + Commands.Help;
    public override string Description => "Terradew test helpers";

    public override void Action(CommandCaller caller, string input, string[] args)
        => caller.Reply(Commands.Run(caller.Player, string.Join(' ', args)));
}
