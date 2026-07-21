# Spyxy's DPS Meter

A compact, always-on-top Windows DPS meter and combat-awareness overlay for **EverQuest Legends**.

Project page: https://github.com/khadesh/SpyxysDPSMeter

## Installer located in the project root:

```text
SpyxysDPSMeter-Setup-1.X.Y-win-x64.exe
```

## Features

### Live EverQuest log monitoring

- Automatically scans the configured folder for files named `eqlog_CHARACTER_SERVER.txt`.
- Selects the newest matching log file by last-write time and file size.
- Reads new log data while EverQuest continues writing to the file.
- Handles log truncation or replacement by reopening the active file.
- Loads up to the latest 1,000 lines when switching logs.
- Displays the active character and server in the title.

The default log folder is:

```text
C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\Logs
```

The folder can be changed from:

```text
Gear menu → Log Directory → Change Log Directory...
```

The selected directory is saved in `settings.json`. Use **Revert to Default** to return to the built-in path.

### Refresh rate

The gear menu provides a **Refresh Rate** option that controls how often the meter checks the active EverQuest log for newly appended lines:

- `0.2s`
- `0.5s`
- `1.0s` — default
- `2.0s`
- `3.0s`
- `5.0s`

The selected rate takes effect immediately and is saved in `settings.json`. Faster rates make new combat events appear sooner but cause the log file to be checked more frequently. When the meter is hidden in the Windows system tray, active-log reading is temporarily reduced to `5.0s` to lower background activity. Restoring the window immediately returns to the player's selected rate without changing the saved setting. This setting changes active-log reading only; the meter still scans periodically for a newer character log.

### Damage and DPS

- Groups damage by attacker.
- Calculates DPS from a rolling 30-second damage window.
- Uses at least one second as the divisor to avoid inflated startup values.
- Tracks direct attacks, spell damage, damage-over-time ticks, and damage-shield damage.
- Damage against the player, the player's pet, or a group member immediately classifies the source as hostile.
- Misses, parries, dodges, blocks, and ripostes update recent targeting during an active encounter.
- A kill creates a three-second encounter barrier:
  - damage inside the barrier continues the current encounter;
  - otherwise the encounter is finalized.
- Rows are sorted by total damage, then alphabetically.

### Damage spell-cast indicators

Temporary red indicators appear beside any recognized entity when it begins casting a damage spell:

- `●` — single-target direct-damage spell;
- `○` — single-target damage-over-time spell;
- `■` — AOE or PBAE direct-damage spell;
- `□` — AOE damage-over-time spell.

All four damage indicators are red and remain visible for four seconds. They can appear for the monitored player, group members, pets, enemies, and other parsed casters.

Damage spells are recognized from built-in direct-damage, DoT, and area-damage spell lists, expanded AE/AOE/PBAE spell-family patterns, and spell names learned from actual damage records during the current run. Al'Kabor spiral spells, rain, column, pillar, circle, tears, Jyll's, and other recognized area families use square markers.

The meter also learns area damage dynamically. When the same source and spell damage multiple distinct targets within a short window, that spell is remembered as area damage for the rest of the run. If its current cast indicator is still visible, the meter immediately changes that indicator from a circle to the appropriate filled or hollow square.

### Teleportation spell indicators

Teleportation and evacuation spells use a golden indicator:

- `◆` — Gate, portal, ring, circle, translocate, evacuate, succor, relocation, or another recognized teleportation spell.

The golden indicator remains visible for four seconds. When **Spell Casting Subtext** is enabled, the teleportation spell name is also shown in the same golden hue. Recognized teleportation spells are excluded from damage-spell classification, including teleport spell families whose names begin with `Circle of`.

### Spell-casting subtext

The **Spell Casting Subtext** display option shows the latest spell being cast beneath an entity's name:

```text
casting Complete Healing
```

The spell name is color-coded using the same categories as the meter's other spell indicators:

- healing — green;
- teleportation and evacuation — golden;
- direct damage and damage-over-time — red;
- charm — bright pink;
- root — bright orange;
- lull / pacify — bright red;
- mesmerize — bluish green;
- stun — bright purple;
- otherwise unclassified spells — bright magenta.

The casting line can appear for the monitored player, group members, pets, enemies, and unknown entities when unknown entities are enabled. It remains visible for three seconds. If the same entity starts another spell during that time, the newer spell immediately replaces the previous one.

Casting subtext is displayed as an additional line, so it can remain visible at the same time as main-assist target or target-mismatch subtext. The toggle is saved in `settings.json`.

### Recent-attacker markers

One or more `!` marks after a name show how many distinct attackers damaged that entity during the last five seconds.

Example:

```text
A goblin scout !!!
```

This means three distinct attackers recently damaged that entity.

### Entity classification and row colors

The meter uses distinct row/text colors for:

- the monitored character;
- known group members;
- the monitored character's pet;
- known enemies;
- unclassified entities.

The **Unknown Entities** setting continuously controls whether unclassified combatants are displayed. When disabled, unknown rows remain filtered out even when new damage, healing, crowd-control, or spell-cast events arrive.

### Immediate hostile classification

An entity is immediately classified as hostile when it:

- deals damage to the monitored character;
- deals damage to the monitored character's pet;
- deals damage to a known or manually tagged group member;
- casts a recognized damaging or crowd-control spell that targets one of those protected entities;
- lands a recognized charm, root, lull, mesmerize, or stun effect on one of those protected entities.

The meter correlates harmful spell effects with recent cast-start messages to identify the caster. The player, owned pets, and known group members are never marked hostile by this rule. Hostile classification no longer waits for the enemy to die, and it remains active even when **Unknown Entities** is disabled.

### Group detection and manual tagging

The meter learns group membership from:

- group invitations and acceptance;
- members joining or leaving;
- group disband/leave messages;
- group chat activity.

The gear menu also provides:

- **Tag Group Member** for manually classifying an unknown entity;
- **Remove Manual Group Member** for removing a manual classification;
- **Always Show Group Members** to keep the player and known group members visible even before they deal damage.

Manual group members are saved in `settings.json`.

### Main assist tools

Right-click a player or group-member row to:

- **Set as Main Assist**
- **Clear Main Assist**

When main-assist indicators are enabled:

- the main assist is underlined and displays a sword icon;
- the main assist's current target appears beneath the name;
- a group member targeting something different displays red target subtext and a flashing warning icon.

The selected main assist is saved between launches.

### Healing indicators

Temporary green indicators appear beside row names:

- `>` — the entity began casting a recognized healing spell;
- `<` — the player or a group member received a heal;
- `<<` — Lay on Hands was received;
- `>>` — the entity cast Lay on Hands on another target.

Timing used by the meter:

| Indicator | Duration |
|---|---:|
| Healing cast `>` | 4 seconds |
| Healing received `<` | 3 seconds |
| Lay on Hands recipient `<<` | 10 seconds |
| Lay on Hands caster `>>` | 12 seconds |

Lay on Hands recipients also receive a flashing green healing label. Multiple temporary indicators can be visible at the same time.

Healing spells are identified from a built-in name list, name fragments, and spell names learned from actual healing log records during the current session.

### Hard crowd-control indicators

The meter recognizes these hard-CC categories:

| Effect | Color |
|---|---|
| Charm | Bright pink |
| Root | Bright orange |
| Lull / Pacify | Bright red |
| Mesmerize | Bluish green |
| Stun | Bright purple |

Indicator shapes:

- `▲` — a normal/single-target CC spell is being cast;
- `■` — a recognized AOE mesmerize or stun is being cast;
- `X` — a target is affected by any recognized CC spell.

Cast indicators appear on the caster for four seconds. Every landed CC effect uses an `X` on the affected target for six seconds, colored according to the CC category. This applies to the monitored player, allies, enemies, and every other recognized target. Temporary indicators fade near expiration.

Recognized CC targets are temporarily added to the table even when they have no damage row.

### Experience tracking

The title can display:

- **XP/h** — experience gained over the current monitored login/session;
- **Last 10 XP/h** — an estimate based on the most recent ten experience gains.

### Platinum tracking

The meter reads currency received from corpses and loot-sale messages.

Two rate modes are available:

- **Normal** — uses retained currency and elapsed time;
- **3m Throttled** — uses a minimum three-minute calculation window to reduce extreme early-session values.

Currency history is retained for up to one hour.

### Display options

The gear menu can toggle:

- player name;
- server name;
- XP/h;
- Last 10 XP/h;
- Platinum/h;
- unknown entities;
- always-visible group members;
- main-assist indicators;
- spell-casting subtext;
- active-log refresh rate.

Damage and DPS numbers can be aligned left or right.

### System tray

- Closing the window hides the meter to the Windows system tray.
- Double-clicking the tray icon restores the window.
- The tray menu provides **Show Spyxy's DPS Meter** and **Exit**.
- Timers and log monitoring continue while the window is hidden.
- While hidden, the active-log refresh interval temporarily changes to `5.0s` to reduce background activity.
- Restoring the window immediately reapplies the refresh rate selected in the gear menu.
- Launching a second copy restores the existing window and immediately closes the new process before it reads any logs.

### Reset button

The reset button clears current:

- damage and DPS;
- encounter state;
- XP tracking;
- platinum tracking;
- target history;
- temporary healing, teleportation, damage-spell, spell-casting subtext, and CC indicators.

It does not erase saved application settings.

### GitHub shortcut

The lower-right corner of the meter always displays **Spyxy's DPS**.

- Hovering shows the full GitHub address.
- Clicking opens the project page in the default browser.

## Getting started

1. Enable EverQuest logging:

   ```text
   /log on
   ```

2. Start Spyxy's DPS Meter.
3. The meter will search the configured log directory for the newest matching log.
4. When necessary, choose another folder from:

   ```text
   Gear menu → Log Directory → Change Log Directory...
   ```

5. Enter combat and watch the table update.

## Settings

Settings are serialized as JSON to:

```text
<application folder>\settings.json
```

The settings file includes display preferences, platinum mode, manual group members, main assist, the spell-casting subtext toggle, the active-log refresh rate, the selected log directory, and the saved window position and size.

Example log-directory setting:

```json
{
  "LogDirectory": "D:\\Games\\EverQuest Legends\\Logs"
}
```

## Building

The project is a Windows WPF application targeting the .NET Windows desktop runtime.

Recommended tools:

- Windows 10 or Windows 11
- Visual Studio with the .NET desktop development workload
- .NET 10 SDK, matching the current project target

Typical command-line build:

```powershell
dotnet restore
dotnet build -c Release
```

Typical self-contained Windows x64 publish:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Troubleshooting

### No log file is detected

- Confirm logging is enabled with `/log on`.
- Open the gear menu and verify the configured log directory.
- Confirm the selected folder contains a file named like:

  ```text
  eqlog_Character_server.txt
  ```

- Use **Revert to Default** if EverQuest Legends is installed in the standard public location.

### The meter is reading the wrong character

The meter selects the newest matching log file. Make the desired character's log the newest by logging into that character or writing a new log line.

### Settings do not save

Confirm the application folder is writable. The current code stores `settings.json` beside the executable.

### An indicator does not appear

Log parsing depends on the exact text written by EverQuest Legends. Capture the relevant raw log lines when reporting an unrecognized spell, heal, CC effect, or combat message.

## Contributing and issue reports

Bug reports and feature requests are welcome at:

https://github.com/khadesh/SpyxysDPSMeter

Useful issue details include:

- the exact raw log line;
- the expected behavior;
- the observed behavior;
- the character class involved;
- whether the effect was single-target or AOE.
