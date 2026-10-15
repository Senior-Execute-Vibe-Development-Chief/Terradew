# Terradew

A Stardew Valley and Terraria mashup. Stardew is the world you see, farm and walk around in. A Terraria
world named **Stardew Valley**, running at the same time in tModLoader, simulates everything Terraria:
creatures, bosses, combat, items, coins, crafting, fishing catches and town NPCs. Neither game is rewritten;
two mods connect them over a local socket.

| Folder | What it is |
|---|---|
| `Terradew/` | SMAPI mod for Stardew Valley 1.6 |
| `TerradewBridge/` | tModLoader mod for Terraria 1.4.4 |
| `TerradewBridge/Core/` | Shared code compiled into both: the socket protocol and the game rules |
| `Terradew.Tests/` | Tests for the shared code (`dotnet run --project Terradew.Tests`) |

## How the two games split the work

- **Stardew** owns the world: maps, walking, farming, the clock, NPC villagers, drawing everything.
- **Terraria** owns the fighting and the loot: creature AI, bosses, projectiles, life, mana, defense, buffs,
  the combat inventory, coins, crafting, shops and town NPCs.
- Each Stardew place gets a fixed rectangle in the Terraria world, at the depth and in the biome it stands
  for. Wherever the farmer cannot walk becomes a Terraria block, so creatures bump into the same walls you do.
  Terraria's own biome rules then decide spawns, music, loot and fishing.

| Stardew place | Terraria |
|---|---|
| Farm, forest, town, mountain and other outdoor places | Surface (forest) |
| Secret Woods | Corruption |
| Calico Desert | Desert |
| Beach | Ocean (placed at the world's edge) |
| Mines 1–30 | Underground |
| Mines 31–60 | Cavern |
| Mines 61–90 | Underground jungle |
| Mines 91–120 | Underworld |
| Skull Cavern floors 1–100 | Dungeon (the Dungeon Guardian waits until Skeletron is beaten) |
| Skull Cavern floors 101+ | Jungle temple (Golem) |
| Ginger Island and the volcano | Their own place: not hell, not jungle |
| Houses, shops and other interiors | No spawns |

Decisions that shape play:

- **Money is Terraria coins.** Stardew's money counter shows your coins, where one gold is one silver coin.
  Shipping crops pays coins; Pierre's seeds cost coins.
- **Health is Terraria's.** Your life, mana, defense and buffs are shown at the top left. Stardew food heals you.
  Dying loses half your coins and wakes you at the Nurse's house, or at home if no Nurse lives on the farm.
- **Only Terraria weapons fight.** Stardew weapons melt into coins when you pick them up, and Stardew's own
  monsters are gone. Farming tools stay Stardew's.
- **Creatures are drawn at true scale** (one Terraria pixel is four Stardew pixels), because Terraria decides
  every hit with its own hitboxes and the picture has to match them.

## Setup

You need Stardew Valley 1.6 with SMAPI 4, and tModLoader.

1. **Build the Stardew mod:** `dotnet build Terradew/Terradew.csproj`. It installs itself into your Stardew
   `Mods` folder. If Stardew is not in the standard Steam or GOG folder, set `STARDEW_PATH` first.
2. **Build the Terraria mod.** tModLoader builds mods from its `ModSources` folder
   (`~/Library/Application Support/Terraria/tModLoader/ModSources` on macOS). Link the mod folder in
   (`TerradewBridge/Core` is a symlink to the shared code, so it comes along):
   ```sh
   ln -s "$PWD/TerradewBridge" "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/TerradewBridge"
   ```
   Then in tModLoader open **Workshop → Develop Mods** and choose **Build** next to Terradew Bridge. If
   tModLoader is not in the standard Steam folder, set `TMOD_PATH`.
3. In tModLoader, enable **only** Terradew Bridge, make a character, and create a **large** world named
   exactly `Stardew Valley`. Terradew clears that world the first time it loads; it never touches any other world.

## Dev launcher (macOS)

`tools/terradew` starts and stops both games through Steam, Terraria first so the bridge is listening:

```sh
tools/terradew start      # tModLoader, then Stardew
tools/terradew stop       # close both
tools/terradew restart    # stop, rebuild the Stardew mod, start again
tools/terradew status     # what is running, and whether the bridge is connected
tools/terradew logs       # follow the SMAPI and tModLoader logs
```

Set `TERRADEW_PLAYER` to your tModLoader character's name to skip the menus and load straight into the world
`Stardew Valley`, for example `TERRADEW_PLAYER=Farmer tools/terradew start`. Stopping closes the games without
saving, which is usually what you want while testing.

## Playing

1. Start tModLoader and enter the world `Stardew Valley`. Leave it running.
2. Start Stardew Valley with SMAPI and load your farm. The top left says when Terraria is connected.
3. The first time a farm links, that Terraria world belongs to it. Another farm will be refused.

| Key | Does |
|---|---|
| `R` | Switch your hand between the Stardew toolbar and the Terraria hotbar |
| `1`–`0`, mouse wheel | Choose a Terraria hotbar slot (Terraria hand) |
| Use tool button | Use the Terraria item, aimed at the cursor (Terraria hand) |
| `I` | Terraria inventory, equipment and crafting |
| Right-click a Terraria townsperson | Talk, shop, or get healed by the Nurse |

Keys can be changed in `Mods/Terradew/config.json`.

- **Crafting** uses stations you carry: craft a Work Bench, Furnace or Anvil and keep it in your inventory.
- **Town NPCs** live in **Terraria Houses**, which Robin builds on the farm. The morning after a house is
  finished, a townsperson whose Terraria conditions are met moves in (the Guide first, the Merchant once you
  carry 50 silver, and so on).
- **Fishing** is Stardew's minigame with a Terraria catch, from the place's biome and depth.
- **Bosses** come from their normal Terraria summons, used with the Terraria hand: the Suspicious Looking Eye at
  night, Worm Food in the Secret Woods, the Abeemination in the jungle mine floors. Talk to the Old Man at the
  Skull Cavern entrance at night for Skeletron. Use the Guide Voodoo Doll on an underworld mine floor for the
  Wall of Flesh. Plantera can wake on a jungle mine floor once the three mechanical bosses are beaten, and a
  Lihzahrd Power Cell wakes Golem in the temple floors.
- **Saving:** Terraria saves its world and character whenever Stardew saves at bedtime.

Test helpers, in the SMAPI console: `terradew status`, `terradew summon`, `terradew summon BlueSlime`,
`terradew boss EyeofCthulhu`, `terradew give CopperShortsword`, `terradew heal`. The same commands work in
Terraria chat as `/terradew ...`.

## Status

The shared code is covered by tests. The two game mods were written without either game available to compile
or run against, so expect a round of compile fixes on the first build, then play-testing. Known gaps:

- Mine floors that need every monster killed for a ladder may need the ladder from a rock instead.
- Hardmode spreads Hallow and Corruption through Terraria tiles; Stardew places are restamped on every visit,
  so that spread does not show on the farm.
- Multiplayer is not supported yet; the protocol is ready for one Terraria character per farmer.
