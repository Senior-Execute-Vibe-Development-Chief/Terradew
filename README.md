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
- **Health is Terraria's.** Your life and mana are bars at the bottom right, where Stardew's energy bar was.
  Defense and buffs are shown at the top left. Energy is gone: tools are free, and you can stay up all night.
  Food heals Terraria life, restores a little mana, and gives Well Fed.
  Dying loses half your coins and wakes you in your own bed. A Terraria hit shows Stardew's hurt blink, red number and
  "ow", and knocks you back from whatever hit you.
- **Only Terraria weapons fight.** Stardew weapons melt into coins when you pick them up, and Stardew's own
  monsters are gone. Farming tools stay Stardew's, but Terraria tools work on Stardew things too: a pickaxe or hammer
  breaks rocks like a Stardew pickaxe, an axe chops trees, and a sword cuts weeds and grass. Stronger Terraria tools
  count as better Stardew upgrades.
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
- **The hotbar is driven from Stardew while the Stardew window is in front.** Click into the Terraria
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
| `1`–`0`, mouse wheel | Choose a hotbar slot; whatever is in it (a Stardew tool or a Terraria weapon) is what you use |
| Use tool button | Use the selected item: a Stardew tool as usual, or a Terraria item aimed at the cursor |
| `I` | Backpack, Terraria equipment and crafting |
| Right-click a Terraria townsperson | Talk, shop, or get healed by the Nurse |

Keys can be changed in `Mods/Terradew/config.json`.

- **One shared backpack:** 50 slots, 10 to a row, Terraria-style; Stardew and Terraria items sit side by side and the
  first row is the toolbar. Stardew's own menus (chests, shops, the shipping bin) show the first 36 slots; Terraria
  items can only be moved in the `I` menu.
- **Crafting stations are placed.** Craft a Work Bench, Furnace, Anvil or any other station, select the
  hotbar slot it is in and use it on a tile next to you to put it down; it is drawn as its Terraria self. The crafting
  menu offers what the stations within four tiles of you allow (and water, if any is that close). Hit a station
  with the Stardew pickaxe or axe to take it back. Stardew's own furniture and decoration stay as they are.
- **Mine barrels and crates are Terraria pots.** They take the look of that floor's biome (hellstone pots in the underworld, dungeon pots in the Skull Cavern) and, as well as Stardew's own loot, spill Terraria pot loot: coins, potions, torches and hearts.
- **Terraria townsfolk play the Stardew villagers.** Each Terraria town NPC moves into one villager's home, the morning after its
  Terraria conditions are met (the Guide first, the Merchant once you carry 50 silver, and so on): the Guide in
  the museum, the Merchant at Pierre's, the Nurse at Harvey's clinic, the Arms Dealer at the Adventurer's Guild,
  the Angler at Willy's, the Goblin Tinkerer at Clint's, the Mechanic and Steampunker at Robin's, the
  Tavernkeep at the Saloon, the Tax Collector at the Mayor's manor, the Zoologist at Marnie's, the Dye Trader at
  Sandy's, the Wizard in the tower, Truffle in Sebastian's basement room (in hardmode), and the rest in the other villagers' houses. The Stardew shops stay open
  where they always were. The full roster of vanilla town NPCs is in play, except Santa Claus.
  Bound townsfolk move in once rescued in Terraria, or at the point vanilla would first leave them to be found: the Tavernkeep after the evil boss, the Stylist and Golfer after the Eye of Cthulhu, the Tax Collector and Wizard in hardmode.
  Once moved in, the NPC takes over that villager's life: it walks their schedule, looks like its Terraria self
  with its Terraria name and a portrait cut from its Terraria sprite, and says its own lines (written in `Terradew/assets/npcs`) for each day,
  heart level, season, rain, festival, and the big things that happen in the Terraria world (a boss falls, a blood
  moon, a goblin army). Friendship, gifts and heart events (2 to 10 hearts) work as in Stardew; Stardew items
  and Terraria items can both be given, and each NPC has its own loved and hated ones. Some heart events give a
  Terraria item. Talk to an NPC a second time on the same day to open its Terraria chat (shop, healing). Each
  heart takes 2% off its shop prices, up to 20%. The Stylist, Painter, Clothier, Golfer, Pirate, Nurse and Truffle
  can be dated and married, with a 14-heart event after the wedding. Villagers with no Terraria NPC (Abigail,
  Caroline, George, Jas, Jodi, Maru, Penny, Sam, Shane, Vincent) are gone, as is any villager whose NPC has not
  moved in yet. Characters outside the town roster (the Dwarf, Gil, Mr. Qi and the like) stay as they are.
- **Townsfolk have courage.** Heroes (Arms Dealer, Pirate, Guide, Demolitionist, Steampunker, Cyborg, Wizard,
  Dryad, Witch Doctor, Tavernkeep) go after enemies they see nearby, up to 20 tiles from where they were, and answer
  calls for help. Guards (Goblin Tinkerer, Mechanic, Dye Trader, Nurse, Zoologist) stop and fight what comes close
  but never chase. Cowards (Merchant, Angler, Princess, Stylist, Tax Collector, Party Girl, Painter, Clothier,
  Golfer, Truffle) run away with a "!", shout for help and the nearest hero comes; they thank whoever saved them.
  Heroes hit harder and take more, cowards hit half as hard, and the Angler, Princess, Stylist, Tax Collector,
  Party Girl and Truffle have no attack at all. Nobody dies: a beaten NPC faints, goes home hurt, and is back the
  next morning. During a boss, an invasion, a pumpkin or frost moon, an eclipse or a blood moon, heroes wait
  outside their front door and everyone else stays home, and each has a line about it. Attacks show their
  Terraria frames, and the Dye Trader swings a scimitar.
- **What you wear shows.** Terraria armor, wings, shields, back and front accessories and shoes are drawn on the
  farmer. Basic dyes tint them by colour; fancy shader dyes are shown undyed. Townspeople show a portrait cut
  from their Terraria sprite when you talk to them.
- **Fishing** is Stardew's minigame with a Terraria catch, from the place's biome and depth.
- **Bosses** come from their normal Terraria summons, used from the hotbar with the Terraria item selected: the Suspicious Looking Eye at
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
- **No passing out.** If you don't go to bed, the clock keeps running past 2:00 AM to 6:00 AM, so Terraria gets its whole night (dusk 7:30 PM, dawn 4:30 AM). At 6:00 AM the day changes (crops grow, shipping is paid, the save screen shows), and then you carry on from where you were standing. Going to bed early still ends the day as normal.
- **Community Center bundles** ask for minerals instead of monster drops, since Stardew's monsters are gone.
- **Saving:** Terraria saves its world and character whenever Stardew saves at the end of the day.

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
- The villagers' own heart events are removed. Other cutscenes and festivals leave out the absent villagers,
  so some of their lines and steps are skipped.
- An NPC whose villager home is on Ginger Island (the Witch Doctor) is only met there.
- Courage only plays out where the farmer is, since creatures elsewhere stand still. After a danger ends,
  villagers pick their schedule up from wherever they are, so some may take odd routes for the rest of the day.
- The harmless cowards lose their attack in every world while Terradew Bridge is loaded.
- Multiplayer is not supported yet; the protocol is ready for one Terraria character per farmer.
