# RA Mods

BepInEx/Harmony mods for Relic Abyss. Each mod builds as a separate plugin DLL, so you can install only the fixes or quality-of-life changes you want.

## Installation

1. Install BepInEx for Unity Mono by following the official guide:
   https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html
2. Start Relic Abyss once, then close it. This lets BepInEx create its folders.
3. Download the DLLs you want from the latest GitHub release.
4. Copy the DLLs into:

```text
<Relic Abyss install folder>\BepInEx\plugins
```

5. Start the game again.

Config files are created under `BepInEx\config` after a plugin has loaded at least once.

## Mods

### Relic Abyss Chest Interaction Priority

Makes chests take priority over nearby equipment or relic pickups when the interaction prompt is choosing what to select. This helps when a chest and dropped loot are close together.

Config: none.

### Relic Abyss Deferred Advancement Shrine

Fixes the advancement shrine menu from opening while the game is not in normal gameplay. If the shrine tries to open at the wrong time, the mod waits and opens it once gameplay is active again.

Config: none.

### Relic Abyss Exp Buff Icon

Adds a small on-screen indicator for active Experience Monolith buffs. The icon shows `EXP` and the current stack count.

Config file: `BepInEx\config\joiny.relicabyss.expbufficon.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Buff Icon` | `X` | `18` | Icon X position in screen pixels. |
| `Buff Icon` | `Y` | `96` | Icon Y position in screen pixels. |
| `Buff Icon` | `IconSize` | `84` | Icon size in screen pixels. Clamped between 56 and 160. |

### Relic Abyss Instant Chest Drops

Skips the chest reward UI and drops chest rewards immediately. Chest rarity still controls the number of reward rolls, and gold is spawned through the game's normal chest gold logic.

Config file: `BepInEx\config\joiny.relicabyss.instantchestdrops.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Chest Drops` | `DropEquipmentOnGround` | `true` | When enabled, equipment rewards always drop on the ground. When disabled, equipment is added to inventory first and only drops if the inventory is full. |

### Relic Abyss Instant Shrines

Activates supported shrine types without opening their dialogue first. Fusion shrines are only instant for the upgrade path; combine fusion keeps the vanilla interaction.

Config file: `BepInEx\config\joiny.relicabyss.instantshrines.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Instant Shrines` | `Random` | `true` | Instantly activate random shrines. |
| `Instant Shrines` | `FusionUpgrade` | `true` | Instantly activate fusion upgrade shrines. Combine fusion remains vanilla. |
| `Instant Shrines` | `Sacrifice` | `true` | Instantly activate sacrifice shrines. |
| `Instant Shrines` | `Experience` | `true` | Instantly activate experience monoliths. |
| `Instant Shrines` | `Advancement` | `true` | Instantly activate advancement shrines. |

### Relic Abyss Magnet Pickup Fix

Calls the pickup magnetize logic for magnetized gold and item pickups so they become collectible while being pulled in.

Config: none.

### Relic Abyss Player Effects Visibility

Adds an effect visibility control for player-owned effects. It changes alpha on player particle systems, sprites, trails, and line renderers while leaving enemy effects alone.

Config file: `BepInEx\config\joiny.relicabyss.playereffectsvisibility.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `Player Effects` | `VisibilityPercent` | `100` | Player-owned effect visibility from 0 to 100. |
| `Player Effects` | `IncludePlayerAttackColliders` | `true` | Also affect spawned objects outside the player pool when they contain a player-owned `AttackCollider`. |
| `Settings UI` | `AddSettingsSlider` | `true` | Add an `Effect Visibility` slider to the game's settings UI. |
| `Settings UI` | `ShowSettingsFallbackOverlay` | `false` | Show a small fallback slider while the settings menu is open. |

### Relic Abyss Training Fix

Patches the training max-rank purchase guard from 9 to 10, allowing the final training rank to be bought.

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
