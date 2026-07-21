# Spyxy's DPS Meter

A compact, always-on-top Windows DPS meter and combat-awareness overlay for **EverQuest Legends**.

Project page: https://github.com/khadesh/SpyxysDPSMeter

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

### Damage and DPS

- Groups damage by attacker.
- Calculates DPS from a rolling 30-second damage window.
- Uses at least one second as the divisor to avoid inflated startup values.
- Tracks direct attacks, spell damage, damage-over-time ticks, and damage-shield damage.
- Misses, parries, dodges, blocks, and ripostes update recent targeting during an active encounter.
- A kill creates a three-second encounter barrier:
  - damage inside the barrier continues the current encounter;
  - otherwise the encounter is finalized.
- Rows are sorted by total damage, then alphabetically.

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

The **Unknown Entities** setting controls whether unclassified combatants are displayed.

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

- `▲` — normal/single-target CC;
- `■` — recognized AOE mesmerize or stun.

A cast indicator is shown on the caster for four seconds. A landed-effect indicator is shown on the affected target for six seconds. Temporary indicators fade near expiration.

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
- main-assist indicators.

Damage and DPS numbers can be aligned left or right.

### System tray

- Closing the window hides the meter to the Windows system tray.
- Double-clicking the tray icon restores the window.
- The tray menu provides **Show Spyxy's DPS Meter** and **Exit**.
- Timers and log monitoring continue while the window is hidden.

### Reset button

The reset button clears current:

- damage and DPS;
- encounter state;
- XP tracking;
- platinum tracking;
- target history;
- temporary healing and CC indicators.

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

The settings file includes display preferences, platinum mode, manual group members, main assist, and the selected log directory.

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
