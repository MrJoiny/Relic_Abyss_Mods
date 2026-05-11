# Relic Abyss Mods

BepInEx/Harmony mods for Relic Abyss. Each mod builds as a separate plugin DLL, so you can install only the fixes or quality-of-life changes you want.

## Installation

1. Install BepInEx for Unity Mono by following the official guide:
   https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html
2. Start Relic Abyss once, then close it. This lets BepInEx create its folders.
3. Download the DLLs you want from the latest GitHub release:
   https://github.com/MrJoiny/Relic_Abyss_Mods/releases/
4. Copy the DLLs into:

```text
<Relic Abyss install folder>\BepInEx\plugins
```

5. Start the game again.

Config files are created under `BepInEx\config` after a plugin has loaded at least once.

## Mods

### Advancement Text Fix

Replaces advancement choice and stats-screen tooltip descriptions with values calculated from the advancement's actual runtime effect. The text accounts for rarity bonuses and already acquired advancement rarities instead of relying on the game's predefined placeholder descriptions.

Config: none.

### Chest Interaction Priority

Makes chests take priority over nearby equipment or relic pickups when the interaction prompt is choosing what to select. This helps when a chest and dropped loot are close together.

Config: none.

### Chest Statue

Adds a Chest statue that can appear in the world. If you pay the shrine, you can send up to three items to the Rewards page in Horizon's End. It uses the built-in ChestStatue dialogue.

Config file: `BepInEx\config\joiny.relicabyss.cheststatue.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Chest Statue` | `Enabled` | `true` | Enables the chest statue shrine. |
| `Spawning` | `SpawnAttemptsPerChunk` | `1` | Chest statue spawn rolls per generated chunk. |
| `Spawning` | `SpawnChancePercent` | `7.5` | Chance for each spawn roll. |

### Deferred Advancement Shrine

Fixes the advancement shrine menu from opening while the game is not in normal gameplay. If the shrine tries to open at the wrong time, the mod waits and opens it once gameplay is active again.

Config: none.

### Exp Buff Icon

Adds a small on-screen indicator for active Experience Monolith buffs. The icon shows `EXP` and the current stack count.

Config file: `BepInEx\config\joiny.relicabyss.expbufficon.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Buff Icon` | `X` | `18` | Icon X position in screen pixels. |
| `Buff Icon` | `Y` | `96` | Icon Y position in screen pixels. |
| `Buff Icon` | `IconSize` | `84` | Icon size in screen pixels. Clamped between 56 and 160. |

### Instant Chest Drops

Skips the chest reward UI and drops chest rewards immediately. Chest rarity still controls the number of reward rolls, and gold is spawned through the game's normal chest gold logic.

Config file: `BepInEx\config\joiny.relicabyss.instantchestdrops.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Chest Drops` | `DropEquipmentOnGround` | `true` | When enabled, equipment rewards always drop on the ground. When disabled, equipment is added to inventory first and only drops if the inventory is full. |

### Hub No Dash Cooldown

Disables the player dash cooldown while in Horizon's End, the hub city with the shops. Combat levels keep the normal dash cooldown.

Config: none.

### Level Up Stats Fix

Makes the new stat-card page appear only on every 5th level-up, prevents it from replacing advancement shrine choices, and makes stat-card banishes stay banished when the page is refreshed.

Config: none.

### Instant Shrines

Activates supported shrine types without opening their dialogue first. Fusion shrines are only instant for the upgrade path; combine fusion keeps the vanilla interaction.

Config file: `BepInEx\config\joiny.relicabyss.instantshrines.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Instant Shrines` | `Random` | `true` | Instantly activate random shrines. |
| `Instant Shrines` | `FusionUpgrade` | `true` | Instantly activate fusion upgrade shrines. Combine fusion remains vanilla. |
| `Instant Shrines` | `Sacrifice` | `true` | Instantly activate sacrifice shrines. |
| `Instant Shrines` | `Experience` | `true` | Instantly activate experience monoliths. |
| `Instant Shrines` | `Advancement` | `true` | Instantly activate advancement shrines. |

### Magnet Pickup Fix

Calls the pickup magnetize logic for magnetized gold and item pickups so they become collectible while being pulled in.

Config: none.

### Manual Dropped Pickups

Makes items dropped from the inventory require manual interaction to collect. Dropped consumables and stackable items still use the game's normal pickup logic once you interact with them, and items that do not fit stay on the ground.

Config: none.

## Building

Requirements:

- .NET SDK
- An installed copy of Relic Abyss
- BepInEx installed into that game folder

The projects reference BepInEx, Harmony, Unity, and `Assembly-CSharp` from your local game install. Point the build at the folder that contains `Relic Abyss_Data` and `BepInEx`.

Set `RELIC_ABYSS_GAME_ROOT` for the current PowerShell session:

```powershell
$env:RELIC_ABYSS_GAME_ROOT = "C:\Path\To\Relic Abyss"
dotnet build RA_Mods.sln -c Release
```

Or pass the path for one build:

```powershell
dotnet build RA_Mods.sln -c Release -p:GameRoot="D:\Path\To\Relic Abyss"
```

The build output for each project is written to that project's `bin\Release` folder. The build also copies the generated DLL into:

```text
<Relic Abyss install folder>\BepInEx\plugins
```

This repository does not include game DLLs or decompiled game source. Keep those files local to your machine.
