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

- **Stardew** owns the world: maps, walking, farming, the clock, the shops, drawing everything.
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
- **Creatures are drawn at Stardew's size** (one Terraria sprite pixel is two Stardew pixels). Terraria shrinks every
  creature and projectile hitbox to half, and only hits on the farmer's own body count, so hits match the picture.
- **Creatures that are chasing you follow you** through a door or warp if they are close behind, but never into
  houses and other places without spawns. Creatures you leave behind wait where they were while Terraria runs;
  hostile creatures are not kept when Terraria closes. Creatures find their way around rocks, fences and
  furniture: they route over the same blocked tiles Terraria sees.
- **Stardew rocks drop Terraria ores** as well as their own, by depth: copper and tin up top, iron, silver and the
  evil ore in the cavern floors, gold and mud in the jungle floors, hellstone in the underworld. Hardmode adds its
  ores (and Skull Cavern holds the best of them), Chlorophyte waits for the three mechanical bosses, and the
  Mountain quarry gives meteorite once the evil boss is down. Mystic stones give life crystals. Chopping wood
  gives Terraria wood for that place.
- **Invasions happen in one place each** and are announced: the Goblin Army attacks Town, the Frost Legion the
  Mountain, the Pirates the Beach, the Martians the Farm. Invaders come in from that map's edges.
- **Hardmode turns the Quarry Mine and the Railroad into the Hallow**, with its own creatures and fish.
- **The Terraria hotbar is driven from Stardew while the Stardew window is in front.** Click into the Terraria
  window to use its own menus (Escape, Settings, Save & Exit).

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
- **Terraria townsfolk replace the Stardew villagers.** The villagers are gone: no talking, gifts, friendship,
  marriage or heart events. Each Terraria town NPC moves into one villager's home instead, the morning after its
  Terraria conditions are met (the Guide first, the Merchant once you carry 50 silver, and so on): the Guide in
  the museum, the Merchant at Pierre's, the Nurse at Harvey's clinic, the Arms Dealer at the Adventurer's Guild,
  the Angler at Willy's, the Goblin Tinkerer at Clint's, the Mechanic and Steampunker at Robin's, the
  Tavernkeep at the Saloon, the Tax Collector at the Mayor's manor, the Zoologist at Marnie's, the Dye Trader at
  Sandy's, the Wizard in the tower, and the rest in the other villagers' houses. The Stardew shops stay open
  where they always were. The full roster of vanilla town NPCs is in play, except Truffle and Santa Claus.
  Bound townsfolk move in once rescued in Terraria, or at the point vanilla would first leave them to be found: the Tavernkeep after the evil boss, the Stylist and Golfer after the Eye of Cthulhu, the Tax Collector and Wizard in hardmode.
- **What you wear shows.** Terraria armor, wings, shields, back and front accessories and shoes are drawn on the
  farmer. Basic dyes tint them by colour; fancy shader dyes are shown undyed. Townspeople show their Terraria
  head portrait when you talk to them.
- **Fishing** is Stardew's minigame with a Terraria catch, from the place's biome and depth.
- **Bosses** come from their normal Terraria summons, used with the Terraria hand: the Suspicious Looking Eye at
  night, Worm Food in the Secret Woods, the Abeemination in the jungle mine floors. Talk to the Old Man at the
  Skull Cavern entrance at night for Skeletron. Use the Guide Voodoo Doll on an underworld mine floor for the
  Wall of Flesh. Plantera can wake on a jungle mine floor once the three mechanical bosses are beaten, and a
  Lihzahrd Power Cell wakes Golem in the temple floors.
- **The Wall of Flesh climbs.** On an underworld mine floor it rises from the south edge and creeps north across
  the whole map, faster as it weakens. The ladder up takes you to the bottom of the floor above, where it starts
  again; the elevator is shut, and floor 91 is the top. Start deep (floor 110 or deeper) to have room to fight.
- **After Golem**, the Lunatic Cultist waits at the Skull Cavern entrance. Beat it and the Lunar Pillars rise,
  one in each place: Solar on the Farm, Vortex in Town, Nebula on the Beach, Stardust on the Mountain. When all
  four fall, Moon Lord comes to the Farm. The Celestial Sigil works only on the Farm.
- **No passing out.** At 2:00 AM the day simply ends, as if you had gone to bed, with no money lost.
- **Community Center bundles** ask for minerals instead of monster drops, since Stardew's monsters are gone.
- **Saving:** Terraria saves its world and character whenever Stardew saves at bedtime.

Test helpers, in the SMAPI console: `terradew status`, `terradew summon`, `terradew summon BlueSlime`,
`terradew boss EyeofCthulhu`, `terradew give CopperShortsword`, `terradew wear CopperHelmet CopperChainmail CopperGreaves`,
`terradew bring Guide` (moves a townsperson next to you, or spawns one), `terradew heal`. The same commands work in
Terraria chat as `/terradew ...`.

## Status

The shared code is covered by tests. The two game mods were written without either game available to compile
or run against, so expect a round of compile fixes on the first build, then play-testing. Known gaps:

- Mine floors that need every monster killed for a ladder may need the ladder from a rock instead.
- Hardmode's Hallow and Corruption do not spread over time; each place keeps the biome it is given.
- Both mods must be the same version: they refuse to link if their protocol numbers differ.
- Festivals and cutscenes may still show the old villagers, and the help-wanted board still posts their requests.
- Multiplayer is not supported yet; the protocol is ready for one Terraria character per farmer.
