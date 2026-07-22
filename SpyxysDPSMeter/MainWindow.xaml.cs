using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace SpyxysDPSMeter
{
    public partial class MainWindow : Window
    {
        private const bool IsDebugMode = false;

        private const int DebugMaximumLogLines = 10000;
        private const int DebugMaximumChatMessages = 10000;
        private const double DebugReplayWindowSeconds = 25.0;
        private const double DebugMaximumSecondsPerLine = 0.0025;

        private const string DefaultLogDirectory =
            @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\Logs";

        private const string ProjectUrl =
            "https://github.com/khadesh/SpyxysDPSMeter";

        private const int MaximumRetainedLogLines = 1000;
        private const int DpsWindowSeconds = 30;
        private const int RecentVictimSeconds = 5;
        private const int FightBarrierSeconds = 3;
        private const int PlatinumHistoryMinutes = 60;
        private const int PlatinumRateWindowMinutes = 3;

        private const int HealingCastIndicatorSeconds = 4;
        private const int HealingReceivedIndicatorSeconds = 3;
        private const int LayOnHandsRecipientSeconds = 10;
        private const int LayOnHandsCasterSeconds = 12;
        private const int CrowdControlCastIndicatorSeconds = 4;
        private const int CrowdControlLandedIndicatorSeconds = 6;
        private const int RecentCrowdControlCastSeconds = 6;
        private const int DamageSpellCastIndicatorSeconds = 4;
        private const int SpellCastingSubtextSeconds = 3;
        private const int HostileSpellCorrelationSeconds = 12;
        private const int AreaDamageLearningWindowSeconds = 2;
        private const int TeleportationSpellCastIndicatorSeconds = 4;
        private const double DefaultLogRefreshIntervalSeconds = 1.0;
        private const double BackgroundLogRefreshIntervalSeconds = 5.0;
        private const int WindowPlacementSaveDelayMilliseconds = 400;

        private const string SingleInstanceMutexName =
            @"Local\SpyxysDPSMeter.SingleInstance";
        private const string SingleInstanceActivationEventName =
            @"Local\SpyxysDPSMeter.ActivateExisting";

        private static readonly string SettingsFilePath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        private const string AttackVerbPattern =
            "hit|hits|" +
            "slash|slashes|" +
            "pierce|pierces|" +
            "crush|crushes|" +
            "claw|claws|" +
            "bite|bites|" +
            "sting|stings|" +
            "punch|punches|" +
            "gore|gores|" +
            "maul|mauls|" +
            "rend|rends|" +
            "smash|smashes|" +
            "strike|strikes|" +
            "slice|slices|" +
            "gouge|gouges|" +
            "burn|burns|" +
            "smite|smites|" +
            "reave|reaves|" +
            "kick|kicks|" +
            "bash|bashes|" +
            "backstab|backstabs|" +
            "frenzy|frenzies|" +
            "slam|slams|" +
            "cleave|cleaves|" +
            "shoot|shoots";

        private static readonly Regex FileNameRegex = new(
            @"^eqlog_(?<character>[^_]+)_(?<server>.+)\.txt$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LogLineRegex = new(
            @"^\[(?<timestamp>[^\]]+)\]\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex DirectDamageRegex = new(
            $@"^(?<source>.+?) (?<verb>{AttackVerbPattern})(?: on)? (?<target>.+?) " +
            @"for (?<amount>\d+) points? of (?:(?<damageType>[A-Za-z-]+) )?damage" +
            @"(?: by (?<ability>.+?))?[.!](?: \((?<modifier>[^)]+)\))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PossessiveSpellRegex = new(
            @"^(?<source>.+?)(?:'s|’s) (?<ability>.+?) (?:hit|hits) " +
            @"(?<target>.+?) for (?<amount>\d+) points? of " +
            @"(?:non-melee )?damage[.!](?: \((?<modifier>[^)]+)\))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DotBySourceRegex = new(
            @"^(?<target>.+?) has taken (?<amount>\d+) damage from " +
            @"(?<ability>.+?) by (?<source>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YourDotRegex = new(
            @"^(?<target>.+?) has taken (?<amount>\d+) damage from your " +
            @"(?<ability>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YourDamageShieldRegex = new(
            @"^(?<target>.+?) (?:is|are) \w+ by YOUR (?<effect>.+?) " +
            @"for (?<amount>\d+) points? of non-melee damage[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PossessiveDamageShieldRegex = new(
            @"^(?<target>.+?) (?:is|are) \w+ by (?<source>.+?)(?:'s|’s) " +
            @"(?<effect>.+?) for (?<amount>\d+) points? of non-melee damage[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AvoidRegex = new(
            $@"^(?<source>.+?) (?:try|tries) to (?<verb>{AttackVerbPattern})(?: on)? " +
            @"(?<target>.+?), but (?:(?<defender>.+?) )?" +
            @"(?<result>miss|misses|parry|parries|dodge|dodges|block|blocks|riposte|ripostes)!" +
            @"(?: \((?<modifier>[^)]+)\))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YouSlainRegex = new(
            @"^You have slain (?<target>.+)!$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SlainByRegex = new(
            @"^(?<target>.+?) has been slain by (?<source>.+)!$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ExperienceRegex = new(
            @"^You gain(?<party> party)? experience! \((?<percent>\d+(?:\.\d+)?)%\)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PetAttackRegex = new(
            @"^(?<pet>.+?) told you, 'Attacking (?<target>.+?) Master\.'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CorpseCurrencyRegex = new(
            @"^You receive (?<money>.+?) from (?:the|.+?(?:'s|’s)) corpse[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SoldLootCurrencyRegex = new(
            @"^You looted .+? and sold it for (?<money>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CoinAmountRegex = new(
            @"(?<amount>\d+)\s+(?<coin>platinum|gold|silver|copper)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupInviteRegex = new(
            @"^(?<member>.+?) invites you to join a group[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupAcceptRegex = new(
            @"^You notify (?<member>.+?) that you agree to join the group[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupMemberJoinedRegex = new(
            @"^(?<member>.+?) (?:has joined|joins) the group[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupMemberLeftRegex = new(
            @"^(?<member>.+?) (?:has left|leaves|has been removed from) the group[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupChatRegex = new(
            @"^(?<member>.+?) tells the group(?:,|:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IncomingTellChatRegex = new(
            @"^(?<sender>.+?) tells you, '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OutgoingTellChatRegex = new(
            @"^You told (?<target>.+?), '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex StructuredChatRegex = new(
            @"^(?<sender>You|.+?) tell(?:s)? (?:the |your )?" +
            @"(?<channel>group|party|guild|raid|fellowship), '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OutgoingStructuredSayChatRegex = new(
            @"^You say to (?:the |your )?" +
            @"(?<channel>group|party|guild|raid|fellowship), '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OutOfCharacterChatRegex = new(
            @"^(?<sender>You|.+?) say(?:s)? out of character, '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AuctionChatRegex = new(
            @"^(?<sender>You|.+?) auction(?:s)?, '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ShoutChatRegex = new(
            @"^(?<sender>You|.+?) shout(?:s)?, '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SayChatRegex = new(
            @"^(?<sender>You|.+?) say(?:s)?, '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NumberedChannelChatRegex = new(
            @"^(?<sender>You|.+?) tell(?:s)? (?<channel>[^,]+), '(?<text>.*)'$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GroupClearedRegex = new(
            @"^(?:You have left the group|You leave the group|" +
            @"You have been removed from the group|You are no longer in a group|" +
            @"Your group has been disbanded|You disband the group)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SelfSpellCastRegex = new(
            @"^You begin(?:s)? (?:casting|singing) (?<spell>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OtherSpellCastRegex = new(
            @"^(?<caster>.+?) begins? (?:casting|singing) (?<spell>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OldSpellCastRegex = new(
            @"^(?<caster>.+?) begins? to cast a spell[.!]\s*<(?<spell>.+?)>[.!]?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SpecialAbilityRegex = new(
            @"^(?<caster>You|.+?) (?:activate|activates|use|uses) " +
            @"(?<spell>Lay (?:on|of) Hands.*?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DirectHealRegex = new(
            @"^(?<healer>.+?) healed (?<target>.+?)(?<hot> over time)? for " +
            @"(?<amount>\d+)(?: \(\d+\))? hit points(?: by (?<spell>.+?))?[.!]" +
            @"(?: \((?<modifier>[^)]+)\))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PassiveHealRegex = new(
            @"^(?<target>.+?) has been healed(?<hot> over time)? for " +
            @"(?<amount>\d+)(?: \(\d+\))? hit points(?: by (?<spell>.+?))?[.!]" +
            @"(?: \((?<modifier>[^)]+)\))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LegacyHealRegex = new(
            @"^(?<target>You|.+?) (?:have|has) been healed for (?<amount>\d+) " +
            @"points?(?: of damage)?(?: by (?<healer>.+?))?[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ReceivedSpellRegex = new(
            @"^(?<target>.+?) (?:is targeted by|becomes the target of) " +
            @"(?<spell>.+?)[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


        private static readonly HashSet<string> HealingSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Heal",
                "Minor Healing",
                "Light Healing",
                "Healing",
                "Greater Healing",
                "Superior Healing",
                "Complete Healing",
                "Remedy",
                "Divine Light",
                "Celestial Healing",
                "Word of Healing",
                "Word of Health",
                "Chloroplast",
                "Chloroblast",
                "Nature's Touch",
                "Nature's Infusion",
                "Karana's Renewal",
                "Tunare's Renewal",
                "Salve",
                "Torpor",
                "Quiescence",
                "Impassivity",
                "Nonchalance",
                "Stoicism",
                "Flowering Heal",
                "Celestial Health",
                "Celestial Echo",
                "Healing Light",
                "Spirit Salve",
                "Kragg's Salve",
                "Bloom",
                "Blooming Heal",
                "Snails Healing",
                "Tortoises Healing",
                "Slugs Healing",
                "Lay on Hands",
                "Lay of Hands"
            };

        private static readonly string[] HealingSpellFragments =
        {
            " heal",
            "healing",
            "remedy",
            "renewal",
            "restoration",
            "rejuven",
            "mending",
            "mend ",
            "salve",
            "chloroblast",
            "chloroplast",
            "nature's touch",
            "nature's infusion",
            "celestial",
            "divine light",
            "word of health",
            "word of healing",
            "torpor",
            "quiescence",
            "recuper",
            "regrowth",
            "regeneration",
            "regenerate",
            "bloom"
        };

        private static readonly HashSet<string> DirectDamageSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Burst of Flame",
                "Burn",
                "Shock of Frost",
                "Shock of Fire",
                "Shock of Lightning",
                "Shock of Ice",
                "Shock of Flame",
                "Shock of Blades",
                "Shock of Spikes",
                "Shock of Swords",
                "Ice Shock",
                "Frost Shock",
                "Lightning Shock",
                "Fire Bolt",
                "Flame Bolt",
                "Lava Bolt",
                "Lightning Bolt",
                "Lightning Blast",
                "Thunder Strike",
                "Thunderclap",
                "Inferno Shock",
                "Force Shock",
                "Conflagration",
                "Sunstrike",
                "Ice Spear of Solist",
                "Spear of Warding",
                "Spear of Molten Shieldstone",
                "Spear of Blistersteel",
                "Spear of Incineration",
                "Draught of Fire",
                "Draught of Ice",
                "Draught of Lightning",
                "Draught of Jiva",
                "Lure of Flame",
                "Lure of Ice",
                "Lure of Lightning",
                "Lure of Frost",
                "Lure of Thunder",
                "Lure of Ro",
                "Scars of Sigil",
                "Porlos' Fury",
                "Combust",
                "Firestrike",
                "Call of Flame",
                "Calefaction",
                "Starfire",
                "Winter's Frost",
                "Lightning Strike",
                "Avalanche",
                "E'ci's Frosty Breath",
                "Karana's Rage",
                "Nature's Wrath",
                "Frost Rift",
                "Frost Strike",
                "Ice Strike",
                "Spirit Strike",
                "Spear of Torment",
                "Blast of Venom",
                "Venomous Blast",
                "Lifetap",
                "Lifespike",
                "Lifedraw",
                "Drain Soul",
                "Drain Spirit",
                "Drain Life",
                "Spirit Tap",
                "Touch of Night",
                "Deflux",
                "Vexing Mordinia",
                "Ancient Lifebane",
                "Poison Bolt",
                "Ignite Bones",
                "Sanity Warp",
                "Chaos Flux",
                "Anarchy",
                "Dementia",
                "Discordant Mind",
                "Mind Melt",
                "Strike",
                "Smite",
                "Wrath",
                "Retribution",
                "Reckoning",
                "Expulse Undead",
                "Dismiss Undead",
                "Banish Undead",
                "Exorcise Undead",
                "Judgment",
                "Holy Strike",
                "Brusco's Boastful Bellow",
                "Denon's Bereavement",
                "Tuyen's Chant of Flame",
                "Tuyen's Chant of Frost",
                "Tuyen's Chant of Poison",
                "Tuyen's Chant of the Plague"
            };

        private static readonly HashSet<string> DamageOverTimeSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Flame Lick",
                "Stinging Swarm",
                "Creeping Crud",
                "Immolate",
                "Drones of Doom",
                "Winged Death",
                "Drifting Death",
                "Breath of Ro",
                "Immolation of Ro",
                "Vengeance of Al'Kabor",
                "Swarming Death",
                "Sicken",
                "Tainted Breath",
                "Affliction",
                "Envenomed Breath",
                "Venom of the Snake",
                "Scourge",
                "Plague",
                "Pox of Bertoxxulous",
                "Bane of Nife",
                "Epidemic",
                "Ebolt",
                "Envenomed Bolt",
                "Disease Cloud",
                "Clinging Darkness",
                "Engulfing Darkness",
                "Dooming Darkness",
                "Cascading Darkness",
                "Heat Blood",
                "Heart Flutter",
                "Boil Blood",
                "Ignite Blood",
                "Bond of Death",
                "Asystole",
                "Splurt",
                "Pyrocruor",
                "Funeral Pyre",
                "Devouring Darkness",
                "Torment of Shadows",
                "Leach",
                "Vampiric Curse",
                "Choke",
                "Suffocating Sphere",
                "Suffocate",
                "Asphyxiate",
                "Torment of Argli",
                "Chords of Dissonance",
                "Denon's Disruptive Discord",
                "Selo's Chords of Cessation",
                "Tuyen's Chant of Flame",
                "Tuyen's Chant of Frost",
                "Tuyen's Chant of Poison",
                "Tuyen's Chant of the Plague"
            };

        private static readonly HashSet<string> AreaDamageSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Column of Lightning",
                "Column of Fire",
                "Column of Frost",
                "Pillar of Lightning",
                "Pillar of Fire",
                "Pillar of Frost",
                "Circle of Force",
                "Circle of Flame",
                "Circle of Winter",
                "Inferno of Al'Kabor",
                "Winds of Gelid",
                "Jyll's Static Pulse",
                "Jyll's Zephyr of Ice",
                "Jyll's Wave of Heat",
                "Rain of Blades",
                "Rain of Spikes",
                "Rain of Swords",
                "Rain of Lava",
                "Rain of Fire",
                "Rain of Lightning",
                "Rain of Molten Lava",
                "Tears of Prexus",
                "Tears of Solusek",
                "Tears of Druzzil",
                "Tremor",
                "Earthquake",
                "Upheaval",
                "Poison Storm",
                "Word of Pain",
                "Word of Shadow",
                "Word of Spirit",
                "Word of Souls",
                "Word of Redemption",
                "Gravity Flux",
                "Color Flux",
                "Color Shift",
                "Color Skew",
                "Color Slant",
                "Color Shock",
                "Chords of Dissonance",
                "Denon's Disruptive Discord",
                "Selo's Chords of Cessation",
                "Numbing Cold",
                "Icestrike",
                "Project Lightning",
                "Fire Spiral of Al'Kabor",
                "Frost Spiral of Al'Kabor",
                "Shock Spiral of Al'Kabor",
                "Force Spiral of Al'Kabor",
                "Energy Storm",
                "Lava Storm",
                "Frost Storm",
                "Lightning Storm",
                "Flame Flux",
                "Cast Force",
                "Thunderclap",
                "Maelstrom of Electricity",
                "Scintillation",
                "Torrent of Ice",
                "Vengeance of Al'Kabor",
                "Retribution of Al'Kabor",
                "Devouring Flames of Al'Kabor",
                "Super Nova",
                "Fire",
                "Lightning Blast",
                "Lightning Strike",
                "Avalanche",
                "Frosty Death",
                "Frosty Death2"
            };

        private static readonly HashSet<string> TeleportationSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Gate",
                "Fade",
                "Yonder",
                "Abscond",
                "Egress",
                "Exodus",
                "Levant",
                "Shadow Step",
                "Markar's Relocation",
                "Markars Relocation",
                "Tishan's Relocation",
                "Wind of the North",
                "Wind of the South",
                "Great Divide",
                "Cobalt Scar",
                "Wakening Lands",
                "Alter Plane Hate",
                "Alter Plane Sky"
            };

        private static readonly HashSet<string> CharmSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Charm",
                "Beguile",
                "Cajoling Whispers",
                "Allure",
                "Boltran's Agacerie",
                "Dictate",
                "Dominate",
                "Domination",
                "Coerce",
                "Alluring Whispers",
                "Solon's Song of the Sirens"
            };

        private static readonly HashSet<string> RootSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Root",
                "Grasping Roots",
                "Ensnaring Roots",
                "Engulfing Roots",
                "Immobilize",
                "Paralyzing Earth",
                "Fetter",
                "Bonds of Force",
                "Earthen Roots",
                "Vinelash"
            };

        private static readonly HashSet<string> LullSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Lull",
                "Soothe",
                "Calm",
                "Pacify",
                "Harmony",
                "Harmony of Nature",
                "Wake of Tranquility",
                "Kelin's Lugubrious Lament"
            };

        private static readonly HashSet<string> MesmerizeSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Mesmerize",
                "Enthrall",
                "Mesmerization",
                "Entrance",
                "Entrancing Lights",
                "Dazzle",
                "Fascination",
                "Glamour of Kintaz",
                "Glamour",
                "Rapture",
                "Kelin's Lucid Lullaby",
                "Crission's Pixie Strike"
            };

        private static readonly HashSet<string> AreaMesmerizeSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Mesmerization",
                "Entrancing Lights",
                "Fascination",
                "Kelin's Lucid Lullaby"
            };

        private static readonly HashSet<string> StunSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Stun",
                "Holy Might",
                "Force",
                "Sound of Force",
                "Sanity Warp",
                "Chaos Flux",
                "Anarchy",
                "Dyn's Dizzying Draught",
                "Whirl Till You Hurl",
                "Color Flux",
                "Color Shift",
                "Color Skew",
                "Color Slant",
                "Color Shock"
            };

        private static readonly HashSet<string> AreaStunSpellNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Color Flux",
                "Color Shift",
                "Color Skew",
                "Color Slant",
                "Color Shock"
            };

        private const string FeatureHelpText = """
Spyxy's DPS Meter — Feature Guide

GETTING STARTED
• Enable EverQuest logging with /log on.
• Start the meter. It automatically searches the configured log directory for the newest eqlog_CHARACTER_SERVER.txt file.
• Change the folder through Gear menu → Log Directory → Change Log Directory....
• Use Revert to Default to return to the built-in EverQuest Legends log path.

DEBUG SNAPSHOT MODE
• Developers can toggle private const bool IsDebugMode at the top of MainWindow.
• false keeps normal live-log behavior.
• true finds the newest available log and reads up to its last 10,000 lines once at startup.
• All parseable lines in that 10,000-line tail are compressed into a recent synthetic timeline of at most 25 seconds.
• Login resets and kill barriers are suppressed during the snapshot load so the parsed tail behaves like one active fight that just happened.
• Catch Tell Window scans the complete selected log and retains the newest 10,000 recognized chat messages for review.
• Debug chat is not written to local chat history. Every debug chat message except Say is marked important without playing 10,000 startup sounds.
• Live reading and the recurring newer-log scan are not started in debug mode.
• The title and status line display DEBUG SNAPSHOT so a test build is easy to recognize.

LOG MONITORING
• The active log is read while EverQuest continues writing to it.
• Log truncation or replacement is detected and recovered automatically.
• The maximum processed lines per poll follows the effective refresh rate:
  – 0.2s: up to 200 lines
  – 0.5s: up to 500 lines
  – 1.0s, 2.0s, 3.0s, or 5.0s: up to 1,000 lines
• The meter retains up to 1,000 recent raw lines internally.
• The monitored character and server can be shown in the title.

REFRESH RATE
• Gear menu → Refresh Rate controls how often the active log is checked.
• Available rates: 0.2s, 0.5s, 1.0s, 2.0s, 3.0s, and 5.0s.
• 1.0s is the default.
• The selected rate and matching line cap apply immediately and are saved in settings.json.
• While hidden in the system tray, the meter temporarily uses 5.0s and the 1,000-line cap.
• Restoring the window immediately returns to the saved foreground rate and its matching cap.
• The separate scan for a newer character log remains on its own schedule.

DATA FILTERING
• Gear menu → Data Filtering provides three mutually exclusive modes:
  – All data: shows and calculates every parsed entity. Best for private instances.
  – Remove unknowns: hides unclassified public players and pets while retaining the player, owned pet, group members, manually tagged members, and known enemies. Combat data continues to be parsed as before.
  – Only knowns: calculates only combat scoped to the player, owned pet, and detected or manually tagged group members.
• In Only knowns, an enemy enters the current encounter scope only after direct damage, an avoided attack, or a recognized harmful spell connects it with the player, owned pet, or group.
• Once scoped, damage is accepted only when both sides of the event are protected friendlies or enemies scoped to that encounter.
• Unrelated public players, pets, and previously known enemies fighting elsewhere are neither displayed nor included in DPS.
• An outsider damaging a scoped enemy is excluded because that outsider was not tagged by the protected group.
• Switching to Only knowns during a fight rebuilds the scope from direct interactions already recorded in the current encounter.
• Older settings migrate automatically: Unknown Entities enabled becomes All data; disabled becomes Remove unknowns.

Catch Tell Window
• Gear menu → Catch Tell Window turns the feature on or off. It is enabled by default.
• The single chat-bubble button in the main title bar opens or restores a separate always-on-top messenger window.
• The badge over the chat button shows the total unread message count.
• Chat is parsed from the active character log without interrupting combat parsing. Recognized pet tells are ignored.
• Tabs are created dynamically for All and every discovered channel, including General, New Players, Say, Group, Guild, Tells, OOC, Auction, Shout, Raid, Fellowship, and custom numbered channels.
• Each channel uses a distinct bubble color. The All tab includes every non-ignored channel.
• Every channel tab has these saved options:
  – Auto mark as read
  – Auto mark as important
  – Mark my name as important
  – Ignore all
• Auto mark as read defaults on for General, New Players, and Say.
• Auto mark as important defaults on for Guild, Group, and Tells.
• Mark my name as important defaults on for discovered channels.
• Ignore all hides the channel from alerts, pending totals, its visible message view, and the All tab, but messages continue to be collected and saved silently so they appear if the channel is enabled again.
• Important unread messages play the Windows exclamation sound.
• Any tab containing important unread messages displays a blinking red !.
• Every tab shows the number of pending messages awaiting review until that channel is opened or all messages are explicitly marked read.
• The All tab never clears messages merely because it is selected or scrolled. Pending messages clear only when their source channel tab is opened, or when Mark all as read is clicked on the All tab.
• Closing the messenger hides it; chat collection continues while the feature remains enabled.
• Chat history is stored as JSON Lines under ChatLogs beside the application, using one character_server.chat.jsonl file per character and server.
• Saved history is retained for a maximum of seven days. Expired entries are removed automatically, and retained history loads as read on startup. Read and important state are intentionally session-only.
• Disabling Catch Tell Window stops new chat collection and hides the window without deleting saved history.

DAMAGE AND DPS
• Damage is grouped by attacker.
• DPS uses a rolling 30-second damage window and never divides by less than one second.
• Direct attacks, spell damage, damage-over-time ticks, and damage-shield damage are tracked.
• Misses, parries, dodges, blocks, and ripostes update recent targeting during an active encounter.
• A kill begins a three-second encounter barrier. Damage within that barrier continues the current encounter; otherwise the encounter is finalized.
• Rows are ordered by total damage and then alphabetically.

DAMAGE-SPELL CAST INDICATORS
• Red ● — single-target direct-damage spell.
• Red ○ — single-target damage-over-time spell.
• Red ■ — AE, AOE, PBAE, or PBAOE direct-damage spell.
• Red □ — AE, AOE, PBAE, or PBAOE damage-over-time spell.
• Damage-cast indicators remain visible for four seconds.
• The meter uses built-in spell lists, known spell-family patterns, and spell names learned from actual damage records.
• When the same source and spell damage multiple targets in a short window, the spell is learned as an area spell for the rest of the run.

TELEPORTATION INDICATORS
• Golden ◆ — recognized Gate, portal, ring, teleport, translocate, evacuation, succor, relocation, or similar movement spell.
• The golden indicator remains visible for four seconds.
• Teleportation classification is checked before damage classification so teleport circles are not mistaken for area damage.

SPELL-CASTING SUBTEXT
• Gear menu → Spell Casting Subtext toggles the latest spell beneath each caster.
• The line appears as “casting Spell Name” and remains visible for three seconds.
• A newer spell immediately replaces the previous spell for that entity.
• Casting text can appear alongside main-assist target or mismatch subtext.
• Spell-name colors:
  – Healing: green
  – Teleportation and evacuation: golden
  – Direct damage and damage over time: red
  – Charm: bright pink
  – Root: bright orange
  – Lull and pacify: bright red
  – Mesmerize: bluish green
  – Stun: bright purple
  – Unclassified spells: bright magenta
• Data Filtering is applied after temporary casting rows are collected, so hidden entities cannot bypass the selected filter.

RECENT-ATTACKER MARKERS
• One or more ! marks after a name show how many distinct attackers damaged that entity during the last five seconds.
• Example: “A goblin scout !!!” means three distinct attackers recently damaged it.

ENTITY CLASSIFICATION
• Yellow identifies the monitored character.
• Gold identifies detected or manually tagged group members.
• Green identifies the monitored character's known pet.
• Red identifies hostile entities.
• Blue identifies entities that have not yet been classified.
• Damage or a recognized harmful effect against the player, owned pet, or group immediately marks its source hostile.
• In Only knowns, a target attacked by the protected group is also scoped and treated as hostile for that encounter.

GROUP DETECTION AND MANUAL TAGGING
• Group invitations, acceptance, joins, departures, disbands, and group chat help maintain the detected group.
• Gear menu → Tag Group Member manually classifies an unknown entity as friendly.
• Gear menu → Remove Manual Group Member removes a manual classification.
• Always Show Group Members keeps the player and known group members visible before they deal damage.
• Manual group members and the selected data-filtering mode are saved in settings.json.

MAIN ASSIST
• Right-click the player or a group-member row and choose Set as Main Assist.
• The main assist is underlined and displays a sword icon.
• The main assist's current target appears beneath the name.
• A group member attacking a different target shows red target subtext and a flashing warning icon.
• Clear Main Assist removes the selection.
• The selected main assist is saved between launches.

HEALING INDICATORS
• Green > — the entity began casting a recognized healing spell; lasts four seconds.
• Green < — the player or a group member received a heal; lasts three seconds.
• Green << — Lay on Hands recipient; lasts ten seconds and adds a flashing LoH label.
• Green >> — Lay on Hands caster; lasts twelve seconds.
• Multiple temporary healing indicators can appear simultaneously.
• Healing spell names are recognized from built-in names, fragments, and actual healing records learned during the current session.

HARD CROWD CONTROL
• Bright pink: charm.
• Bright orange: root.
• Bright red: lull or pacify.
• Bluish green: mesmerize.
• Bright purple: stun.
• ▲ on the caster means a normal or single-target CC spell is being cast.
• ■ on the caster means a recognized AOE mesmerize or stun is being cast.
• X on a target means that target is affected by a recognized CC spell.
• Cast markers remain for four seconds; landed X markers remain for six seconds.
• A harmful CC interaction with the protected group can immediately scope and classify its caster in Only knowns.

EXPERIENCE
• XP/h shows experience gained during the current monitored login/session.
• Last 10 XP/h estimates the rate from the most recent ten experience gains.

PLATINUM
• The meter reads currency received from corpses and loot-sale messages.
• Normal mode uses retained currency and elapsed time.
• 3m Throttled mode uses a minimum three-minute window to reduce extreme early-session values.
• Currency history is retained for up to one hour.

DISPLAY OPTIONS
• The gear menu can toggle player name, server name, XP/h, Last 10 XP/h, Platinum/h, always-visible group members, main-assist indicators, spell-casting subtext, and Catch Tell Window.
• Data Filtering replaces the former Unknown Entities toggle.
• Damage and DPS values can be aligned left or right.
• Window position and size are saved automatically.

SYSTEM TRAY AND SINGLE INSTANCE
• Closing the window hides the meter in the Windows system tray; monitoring continues.
• Double-click the tray icon or choose Show Spyxy's DPS Meter to restore it.
• The tray menu also provides Exit.
• While hidden, the active-log refresh interval temporarily changes to 5.0s and uses the 1,000-line cap.
• Launching another copy restores the existing hidden or minimized window, then closes the new process before it reads logs.

RESET
• The reset button clears current damage, DPS, encounter scope, XP, platinum, target history, healing indicators, teleport indicators, damage-spell indicators, casting subtext, and crowd-control indicators.
• Reset does not erase saved application settings or Catch Tell Window history, tabs, unread state, or channel preferences.

SETTINGS AND TROUBLESHOOTING
• Settings are stored in settings.json beside the executable.
• DataFilteringMode stores AllData, RemoveUnknowns, or OnlyKnowns.
• InstantMessengerEnabled and InstantMessengerChannelSettings store messenger availability and per-channel behavior.
• Existing ShowUnknownEntities settings are migrated automatically.
• If no log is detected, verify /log on and confirm the selected directory contains an eqlog_Character_server.txt file.
• The newest matching log is selected. Log into or generate a new line for the desired character if the wrong character is active.
• If settings do not save, confirm the application folder is writable.
• Unrecognized effects depend on exact EverQuest Legends log text. Include the raw log line when reporting an issue.

PROJECT
• GitHub: https://github.com/khadesh/SpyxysDPSMeter
• The Spyxy's DPS link in the lower-right corner opens the project page.
""";

        private static readonly string[] TimestampFormats =
        {
            "ddd MMM d HH:mm:ss yyyy",
            "ddd MMM dd HH:mm:ss yyyy"
        };

        private static readonly global::System.Windows.Media.Brush SelfRowBrush = FrozenBrush(90, 112, 83, 18);
        private static readonly global::System.Windows.Media.Brush SelfTextBrush = FrozenBrush(255, 255, 216, 92);

        private static readonly global::System.Windows.Media.Brush GroupRowBrush = FrozenBrush(90, 94, 74, 28);
        private static readonly global::System.Windows.Media.Brush GroupTextBrush = FrozenBrush(255, 246, 220, 137);

        private static readonly global::System.Windows.Media.Brush PetRowBrush = FrozenBrush(90, 35, 102, 50);
        private static readonly global::System.Windows.Media.Brush PetTextBrush = FrozenBrush(255, 142, 232, 148);

        private static readonly global::System.Windows.Media.Brush EnemyRowBrush = FrozenBrush(90, 111, 34, 34);
        private static readonly global::System.Windows.Media.Brush EnemyTextBrush = FrozenBrush(255, 255, 142, 142);

        private static readonly global::System.Windows.Media.Brush FriendlyRowBrush = FrozenBrush(90, 37, 76, 115);
        private static readonly global::System.Windows.Media.Brush FriendlyTextBrush = FrozenBrush(255, 161, 207, 255);

        private static readonly global::System.Windows.Media.Brush HealingIndicatorBrush =
            FrozenBrush(255, 89, 255, 138);
        private static readonly global::System.Windows.Media.Brush DamageSpellIndicatorBrush =
            FrozenBrush(255, 255, 76, 76);
        private static readonly global::System.Windows.Media.Brush UnclassifiedSpellIndicatorBrush =
            FrozenBrush(255, 255, 74, 224);
        private static readonly global::System.Windows.Media.Brush TeleportationSpellIndicatorBrush =
            FrozenBrush(255, 255, 196, 64);
        private static readonly global::System.Windows.Media.Brush CharmIndicatorBrush =
            FrozenBrush(255, 255, 79, 216);
        private static readonly global::System.Windows.Media.Brush RootIndicatorBrush =
            FrozenBrush(255, 255, 157, 46);
        private static readonly global::System.Windows.Media.Brush LullIndicatorBrush =
            FrozenBrush(255, 255, 59, 59);
        private static readonly global::System.Windows.Media.Brush MesmerizeIndicatorBrush =
            FrozenBrush(255, 49, 230, 208);
        private static readonly global::System.Windows.Media.Brush StunIndicatorBrush =
            FrozenBrush(255, 200, 107, 255);

        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _singleInstanceActivationEvent;
        private static RegisteredWaitHandle? _singleInstanceActivationRegistration;
        private static MainWindow? _primaryWindow;
        private static bool _singleInstanceInitialized;

        private readonly ObservableCollection<DamageRow> _rows = new();
        private readonly DispatcherTimer _readTimer;
        private readonly DispatcherTimer _fileScanTimer;
        private readonly DispatcherTimer _windowSettingsSaveTimer;

        private global::System.Windows.Forms.NotifyIcon? _trayIcon;
        private global::System.Windows.Forms.ContextMenuStrip? _trayMenu;
        private global::System.Drawing.Icon? _trayIconImage;
        private bool _exitRequested;
        private bool _windowHasLoaded;
        private bool _isApplyingWindowPlacement;
        private bool _isLoadingDebugSnapshot;

        private InstantMessengerWindow? _instantMessengerWindow;
        private bool _instantMessengerEnabled = true;
        private readonly Dictionary<
            string,
            InstantMessengerChannelPreference>
            _instantMessengerChannelSettings =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly List<DamageEvent> _fightDamageEvents = new();
        private readonly List<ExperienceEvent> _experienceEvents = new();
        private readonly List<CurrencyEvent> _currencyEvents = new();
        private readonly Queue<CachedLogLine> _recentLogLines = new();
        private readonly Dictionary<string, string> _petOwners =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _knownBadGuys =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _onlyKnownCombatEntities =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _groupMembers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _manualGroupMembers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TargetEvent> _latestTargets =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly List<SpecialIndicatorEvent> _specialIndicatorEvents = new();
        private readonly List<RecentCrowdControlCast> _recentCrowdControlCasts = new();
        private readonly List<RecentLayOnHandsCast> _recentLayOnHandsCasts = new();
        private readonly Dictionary<string, DateTime> _healingAnimationUntil =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RecentSpellCast> _latestSpellCasts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RecentSpellCastSource> _recentSpellCastSources = new();
        private readonly HashSet<string> _learnedHealingSpellNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _learnedDirectDamageSpellNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _learnedDamageOverTimeSpellNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _learnedAreaDamageSpellNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RecentAreaDamageObservation>
            _recentAreaDamageObservations =
                new(StringComparer.OrdinalIgnoreCase);

        private string _logDirectory = DefaultLogDirectory;
        private string? _activeFilePath;
        private string _characterName = "Character";
        private string _serverName = "Server";
        private string _pendingText = string.Empty;

        private long _fileOffset;
        private DateTime _lastWriteTimeUtc;
        private DateTime? _latestLogTimestamp;
        private DateTime? _sessionStart;

        private bool _fightActive;
        private DateTime? _fightStart;
        private DateTime? _fightEnd;
        private DateTime? _lastCombatActivity;
        private DateTime? _pendingBarrierTimestamp;
        private DateTime? _pendingBarrierWallClock;
        private DateTime? _pendingPartyExperienceTimestamp;

        private string? _pendingGroupInviter;

        private bool _showPlayerName = true;
        private bool _showServerName = true;
        private bool _showExperiencePerHour = true;
        private bool _showLastTenExperience = true;
        private bool _showPlatinumPerHour = true;
        private DataFilterMode _dataFilterMode =
            DataFilterMode.AllData;
        private bool _numbersRightAligned;
        private bool _useThrottledPlatinumRate = true;
        private bool _alwaysShowGroupMembers = true;
        private bool _showMainAssistIndicators = true;
        private bool _showSpellCastingSubtext = true;
        private double _logRefreshIntervalSeconds =
            DefaultLogRefreshIntervalSeconds;
        private string? _mainAssistName;

        public MainWindow()
        {
            EnsureSingleInstanceOrExit();

            InitializeComponent();
            _primaryWindow = this;

            DpsGrid.ItemsSource = _rows;

            _windowSettingsSaveTimer =
                new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(
                        WindowPlacementSaveDelayMilliseconds)
                };
            _windowSettingsSaveTimer.Tick +=
                WindowSettingsSaveTimer_Tick;

            LoadSettings();
            ApplySettingsToMenuItems();

            LocationChanged += WindowPlacementChanged;
            SizeChanged += WindowPlacementChanged;

            _readTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(
                    _logRefreshIntervalSeconds)
            };
            _readTimer.Tick += ReadTimer_Tick;

            _fileScanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _fileScanTimer.Tick += FileScanTimer_Tick;

            InitializeTrayIcon();
            StartSingleInstanceActivationListener();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHasLoaded = true;
            EnsureWindowVisible();

            if (IsDebugMode)
            {
                LoadDebugSnapshotFromLatestLog();
                return;
            }

            DetectLatestLogFile();
            _readTimer.Start();
            _fileScanTimer.Start();
            RefreshDisplay();
        }

        private void Window_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (_exitRequested)
            {
                return;
            }

            e.Cancel = true;
            HideToSystemTray();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _readTimer.Stop();
            _fileScanTimer.Stop();
            _windowSettingsSaveTimer.Stop();

            SaveSettings();
            _instantMessengerWindow?.Shutdown();
            _instantMessengerWindow = null;
            DisposeTrayIcon();
            DisposeSingleInstanceResources();
        }


        private static void EnsureSingleInstanceOrExit()
        {
            if (_singleInstanceInitialized)
            {
                return;
            }

            _singleInstanceInitialized = true;

            try
            {
                _singleInstanceMutex =
                    new Mutex(
                        initiallyOwned: false,
                        SingleInstanceMutexName,
                        out bool createdNew);

                if (!createdNew)
                {
                    SignalExistingInstance();
                    Environment.Exit(0);
                    return;
                }

                _singleInstanceActivationEvent =
                    new EventWaitHandle(
                        initialState: false,
                        EventResetMode.AutoReset,
                        SingleInstanceActivationEventName);
            }
            catch
            {
                // If Windows refuses the named synchronization objects,
                // continue normally rather than preventing the meter from
                // starting at all.
            }
        }

        private static void SignalExistingInstance()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using EventWaitHandle activationEvent =
                        EventWaitHandle.OpenExisting(
                            SingleInstanceActivationEventName);

                    activationEvent.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    return;
                }
            }
        }

        private static void StartSingleInstanceActivationListener()
        {
            if (_singleInstanceActivationEvent == null ||
                _singleInstanceActivationRegistration != null)
            {
                return;
            }

            _singleInstanceActivationRegistration =
                ThreadPool.RegisterWaitForSingleObject(
                    _singleInstanceActivationEvent,
                    static (_, timedOut) =>
                    {
                        if (timedOut)
                        {
                            return;
                        }

                        global::System.Windows.Application? application =
                            global::System.Windows.Application.Current;

                        if (application == null ||
                            application.Dispatcher.HasShutdownStarted ||
                            application.Dispatcher.HasShutdownFinished)
                        {
                            return;
                        }

                        application.Dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            new Action(
                                () =>
                                    _primaryWindow?
                                        .RestoreFromSecondInstance()));
                    },
                    state: null,
                    millisecondsTimeOutInterval: Timeout.Infinite,
                    executeOnlyOnce: false);
        }

        private void RestoreFromSecondInstance()
        {
            ShowFromSystemTray();

            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(
                    () =>
                    {
                        if (WindowState == WindowState.Minimized)
                        {
                            WindowState = WindowState.Normal;
                        }

                        Activate();
                        Focus();
                    }));
        }

        private static void DisposeSingleInstanceResources()
        {
            _primaryWindow = null;

            _singleInstanceActivationRegistration?
                .Unregister(null);
            _singleInstanceActivationRegistration = null;

            _singleInstanceActivationEvent?.Dispose();
            _singleInstanceActivationEvent = null;

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }


        private void WindowPlacementChanged(
            object? sender,
            EventArgs e)
        {
            QueueWindowPlacementSave();
        }

        private void QueueWindowPlacementSave()
        {
            if (!_windowHasLoaded ||
                _isApplyingWindowPlacement ||
                WindowState != WindowState.Normal)
            {
                return;
            }

            _windowSettingsSaveTimer.Stop();
            _windowSettingsSaveTimer.Start();
        }

        private void WindowSettingsSaveTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _windowSettingsSaveTimer.Stop();
            SaveSettings();
        }

        private void ApplySavedWindowPlacement(
            MeterSettings settings)
        {
            _isApplyingWindowPlacement = true;

            try
            {
                if (IsValidWindowDimension(
                        settings.WindowWidth,
                        MinWidth))
                {
                    Width = settings.WindowWidth!.Value;
                }

                if (IsValidWindowDimension(
                        settings.WindowHeight,
                        MinHeight))
                {
                    Height = settings.WindowHeight!.Value;
                }

                if (IsFinite(settings.WindowLeft) &&
                    IsFinite(settings.WindowTop))
                {
                    WindowStartupLocation =
                        WindowStartupLocation.Manual;
                    Left = settings.WindowLeft!.Value;
                    Top = settings.WindowTop!.Value;
                }
            }
            finally
            {
                _isApplyingWindowPlacement = false;
            }
        }

        private void EnsureWindowVisible()
        {
            if (!IsFinite(Left) ||
                !IsFinite(Top))
            {
                return;
            }

            double width =
                ActualWidth > 0
                    ? ActualWidth
                    : Width;
            double height =
                ActualHeight > 0
                    ? ActualHeight
                    : Height;

            if (!IsValidWindowDimension(width, MinWidth) ||
                !IsValidWindowDimension(height, MinHeight))
            {
                return;
            }

            global::System.Drawing.Rectangle windowRectangle =
                new(
                    (int)Math.Floor(Left),
                    (int)Math.Floor(Top),
                    Math.Max(1, (int)Math.Ceiling(width)),
                    Math.Max(1, (int)Math.Ceiling(height)));

            bool sufficientlyVisible =
                global::System.Windows.Forms.Screen.AllScreens
                    .Any(
                        screen =>
                        {
                            global::System.Drawing.Rectangle intersection =
                                global::System.Drawing.Rectangle.Intersect(
                                    windowRectangle,
                                    screen.WorkingArea);

                            return intersection.Width >= 80 &&
                                   intersection.Height >= 38;
                        });

            if (sufficientlyVisible)
            {
                return;
            }

            _isApplyingWindowPlacement = true;

            try
            {
                Rect workArea =
                    SystemParameters.WorkArea;

                Left =
                    workArea.Left +
                    Math.Max(
                        0,
                        (workArea.Width - width) / 2);
                Top =
                    workArea.Top +
                    Math.Max(
                        0,
                        (workArea.Height - height) / 2);
            }
            finally
            {
                _isApplyingWindowPlacement = false;
            }

            QueueWindowPlacementSave();
        }

        private Rect GetWindowPlacementBounds()
        {
            Rect bounds =
                WindowState == WindowState.Normal
                    ? new Rect(
                        Left,
                        Top,
                        ActualWidth > 0
                            ? ActualWidth
                            : Width,
                        ActualHeight > 0
                            ? ActualHeight
                            : Height)
                    : RestoreBounds;

            if (!IsFinite(bounds.Left) ||
                !IsFinite(bounds.Top) ||
                !IsValidWindowDimension(
                    bounds.Width,
                    MinWidth) ||
                !IsValidWindowDimension(
                    bounds.Height,
                    MinHeight))
            {
                return Rect.Empty;
            }

            return bounds;
        }

        private static bool IsFinite(
            double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static bool IsFinite(
            double? value)
        {
            return value.HasValue &&
                   IsFinite(value.Value);
        }

        private static bool IsValidWindowDimension(
            double value,
            double minimum)
        {
            return IsFinite(value) &&
                   value >= minimum;
        }

        private static bool IsValidWindowDimension(
            double? value,
            double minimum)
        {
            return value.HasValue &&
                   IsValidWindowDimension(
                       value.Value,
                       minimum);
        }

        private void FileScanTimer_Tick(object? sender, EventArgs e)
        {
            DetectLatestLogFile();
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new global::System.Windows.Forms.ContextMenuStrip();

            global::System.Windows.Forms.ToolStripMenuItem showItem = new(
                "Show Spyxy's DPS Meter");
            showItem.Click += (_, _) =>
                Dispatcher.Invoke(ShowFromSystemTray);

            global::System.Windows.Forms.ToolStripMenuItem exitItem = new("Exit");
            exitItem.Click += (_, _) =>
                Dispatcher.Invoke(ExitApplication);

            _trayMenu.Items.Add(showItem);
            _trayMenu.Items.Add(new global::System.Windows.Forms.ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayIconImage = LoadTrayIcon();

            _trayIcon = new global::System.Windows.Forms.NotifyIcon
            {
                Text = "Spyxy's DPS Meter",
                Icon = _trayIconImage,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) =>
                Dispatcher.Invoke(ShowFromSystemTray);
        }

        private static global::System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                string? executablePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    global::System.Drawing.Icon? extracted =
                        global::System.Drawing.Icon.ExtractAssociatedIcon(
                            executablePath);

                    if (extracted != null)
                    {
                        return extracted;
                    }
                }
            }
            catch
            {
                // Fall back to the standard Windows application icon.
            }

            return (global::System.Drawing.Icon)
                global::System.Drawing.SystemIcons.Application.Clone();
        }

        private void HideToSystemTray()
        {
            _windowSettingsSaveTimer.Stop();
            SaveSettings();

            ShowInTaskbar = false;
            Hide();

            ApplyReadTimerInterval(
                BackgroundLogRefreshIntervalSeconds);
        }

        private void ShowFromSystemTray()
        {
            ShowInTaskbar = true;

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            ApplyReadTimerInterval(
                _logRefreshIntervalSeconds);

            Activate();
            Focus();
        }

        private void ApplyReadTimerInterval(
            double intervalSeconds)
        {
            _readTimer.Interval =
                TimeSpan.FromSeconds(
                    NormalizeLogRefreshIntervalSeconds(
                        intervalSeconds));
        }

        private void ExitApplication()
        {
            _exitRequested = true;
            Close();
            global::System.Windows.Application.Current.Shutdown();
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;

            _trayIconImage?.Dispose();
            _trayIconImage = null;
        }

        private void ReadTimer_Tick(object? sender, EventArgs e)
        {
            ReadAppendedLogData();
            TryFinalizePendingBarrierByWallClock();
            PruneOldDamageEvents();
            RefreshDisplay();
        }

        private void EnsureInstantMessengerWindow()
        {
            if (_instantMessengerWindow != null)
            {
                return;
            }

            _instantMessengerWindow =
                new InstantMessengerWindow(
                    _instantMessengerChannelSettings);

            _instantMessengerWindow.UnreadStateChanged +=
                InstantMessengerWindow_UnreadStateChanged;
            _instantMessengerWindow.ChannelSettingsChanged +=
                InstantMessengerWindow_ChannelSettingsChanged;
        }

        private void InitializeInstantMessengerForCurrentCharacter()
        {
            UpdateInstantMessengerButtonState();

            if (!_instantMessengerEnabled)
            {
                return;
            }

            EnsureInstantMessengerWindow();

            _instantMessengerWindow?.SetIdentity(
                _characterName,
                _serverName);
        }

        private void InstantMessengerWindow_UnreadStateChanged(
            int unreadCount,
            bool hasImportant)
        {
            if (!_instantMessengerEnabled)
            {
                InstantMessengerUnreadBadge.Visibility =
                    Visibility.Collapsed;
                return;
            }

            InstantMessengerUnreadText.Text =
                unreadCount > 99
                    ? "99+"
                    : unreadCount.ToString(
                        CultureInfo.InvariantCulture);

            InstantMessengerUnreadBadge.Visibility =
                unreadCount > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            InstantMessengerUnreadBadge.Background =
                hasImportant
                    ? FrozenBrush(
                        255,
                        220,
                        62,
                        62)
                    : FrozenBrush(
                        255,
                        42,
                        137,
                        214);
        }

        private void InstantMessengerWindow_ChannelSettingsChanged()
        {
            SaveSettings();
        }

        private void InstantMessengerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_instantMessengerEnabled)
            {
                SetStatus(
                    "Catch Tell Window is disabled in the gear menu.");
                return;
            }

            InitializeInstantMessengerForCurrentCharacter();
            _instantMessengerWindow?.ShowMessenger();
        }

        private void InstantMessengerSettingMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not
                    global::System.Windows.Controls.MenuItem menuItem)
            {
                return;
            }

            _instantMessengerEnabled =
                menuItem.IsChecked;

            if (_instantMessengerEnabled)
            {
                InitializeInstantMessengerForCurrentCharacter();
                _instantMessengerWindow?.PublishUnreadState();
                SetStatus(
                    "Catch Tell Window enabled.");
            }
            else
            {
                _instantMessengerWindow?.HideMessenger();
                InstantMessengerUnreadBadge.Visibility =
                    Visibility.Collapsed;
                SetStatus(
                    "Catch Tell Window disabled. New chat lines will be ignored.");
            }

            UpdateInstantMessengerButtonState();
            SaveSettings();
        }

        private void UpdateInstantMessengerButtonState()
        {
            InstantMessengerButton.IsEnabled =
                _instantMessengerEnabled;
            InstantMessengerButton.Opacity =
                _instantMessengerEnabled
                    ? 1
                    : 0.35;
            InstantMessengerButton.ToolTip =
                _instantMessengerEnabled
                    ? "Open Catch Tell Window"
                    : "Catch Tell Window is disabled in settings";

            if (!_instantMessengerEnabled)
            {
                InstantMessengerUnreadBadge.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void CaptureInstantMessengerMessage(
            string message,
            DateTime timestamp)
        {
            if (!_instantMessengerEnabled ||
                !TryParseInstantMessengerMessage(
                    message,
                    timestamp,
                    out InstantMessageRecord? chatMessage))
            {
                return;
            }

            InitializeInstantMessengerForCurrentCharacter();
            _instantMessengerWindow?.AddLiveMessage(
                chatMessage!);
        }

        private bool TryParseInstantMessengerMessage(
            string message,
            DateTime timestamp,
            out InstantMessageRecord? chatMessage)
        {
            chatMessage = null;

            if (string.IsNullOrWhiteSpace(
                    message) ||
                PetAttackRegex.IsMatch(
                    message))
            {
                return false;
            }

            Match match =
                IncomingTellChatRegex.Match(
                    message);

            if (match.Success)
            {
                string sender =
                    NormalizeEntity(
                        match.Groups["sender"].Value);

                if (IsOwnedPet(sender))
                {
                    return false;
                }

                chatMessage =
                    CreateInstantMessageRecord(
                        timestamp,
                        "Tells",
                        sender,
                        _characterName,
                        match.Groups["text"].Value,
                        isOutgoing: false);

                return true;
            }

            match =
                OutgoingTellChatRegex.Match(
                    message);

            if (match.Success)
            {
                chatMessage =
                    CreateInstantMessageRecord(
                        timestamp,
                        "Tells",
                        _characterName,
                        NormalizeEntity(
                            match.Groups["target"].Value),
                        match.Groups["text"].Value,
                        isOutgoing: true);

                return true;
            }

            match =
                StructuredChatRegex.Match(
                    message);

            if (match.Success)
            {
                string rawSender =
                    match.Groups["sender"].Value;
                bool isOutgoing =
                    rawSender.Equals(
                        "You",
                        StringComparison.OrdinalIgnoreCase);
                string sender =
                    isOutgoing
                        ? _characterName
                        : NormalizeEntity(
                            rawSender);

                if (!isOutgoing &&
                    IsOwnedPet(sender))
                {
                    return false;
                }

                string channel =
                    NormalizeInstantMessengerChannelName(
                        match.Groups["channel"].Value);

                chatMessage =
                    CreateInstantMessageRecord(
                        timestamp,
                        channel,
                        sender,
                        null,
                        match.Groups["text"].Value,
                        isOutgoing);

                return true;
            }

            match =
                OutgoingStructuredSayChatRegex.Match(
                    message);

            if (match.Success)
            {
                chatMessage =
                    CreateInstantMessageRecord(
                        timestamp,
                        NormalizeInstantMessengerChannelName(
                            match.Groups["channel"].Value),
                        _characterName,
                        null,
                        match.Groups["text"].Value,
                        isOutgoing: true);

                return true;
            }

            match =
                OutOfCharacterChatRegex.Match(
                    message);

            if (match.Success)
            {
                return TryCreateSimpleInstantMessage(
                    match,
                    timestamp,
                    "OOC",
                    out chatMessage);
            }

            match =
                AuctionChatRegex.Match(
                    message);

            if (match.Success)
            {
                return TryCreateSimpleInstantMessage(
                    match,
                    timestamp,
                    "Auction",
                    out chatMessage);
            }

            match =
                ShoutChatRegex.Match(
                    message);

            if (match.Success)
            {
                return TryCreateSimpleInstantMessage(
                    match,
                    timestamp,
                    "Shout",
                    out chatMessage);
            }

            match =
                SayChatRegex.Match(
                    message);

            if (match.Success)
            {
                return TryCreateSimpleInstantMessage(
                    match,
                    timestamp,
                    "Say",
                    out chatMessage);
            }

            match =
                NumberedChannelChatRegex.Match(
                    message);

            if (!match.Success)
            {
                return false;
            }

            string rawChannel =
                match.Groups["channel"].Value;

            if (rawChannel.Equals(
                    "you",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string numberedSender =
                match.Groups["sender"].Value;
            bool numberedOutgoing =
                numberedSender.Equals(
                    "You",
                    StringComparison.OrdinalIgnoreCase);
            string normalizedSender =
                numberedOutgoing
                    ? _characterName
                    : NormalizeEntity(
                        numberedSender);

            if (!numberedOutgoing &&
                IsOwnedPet(normalizedSender))
            {
                return false;
            }

            chatMessage =
                CreateInstantMessageRecord(
                    timestamp,
                    NormalizeInstantMessengerChannelName(
                        rawChannel),
                    normalizedSender,
                    null,
                    match.Groups["text"].Value,
                    numberedOutgoing);

            return true;
        }

        private bool TryCreateSimpleInstantMessage(
            Match match,
            DateTime timestamp,
            string channel,
            out InstantMessageRecord? chatMessage)
        {
            string rawSender =
                match.Groups["sender"].Value;
            bool isOutgoing =
                rawSender.Equals(
                    "You",
                    StringComparison.OrdinalIgnoreCase);
            string sender =
                isOutgoing
                    ? _characterName
                    : NormalizeEntity(
                        rawSender);

            if (!isOutgoing &&
                IsOwnedPet(sender))
            {
                chatMessage = null;
                return false;
            }

            chatMessage =
                CreateInstantMessageRecord(
                    timestamp,
                    channel,
                    sender,
                    null,
                    match.Groups["text"].Value,
                    isOutgoing);

            return true;
        }

        private static InstantMessageRecord CreateInstantMessageRecord(
            DateTime timestamp,
            string channel,
            string sender,
            string? recipient,
            string text,
            bool isOutgoing)
        {
            return new InstantMessageRecord
            {
                Timestamp = timestamp,
                Channel =
                    NormalizeInstantMessengerChannelName(
                        channel),
                Sender =
                    string.IsNullOrWhiteSpace(
                        sender)
                        ? "Unknown"
                        : sender.Trim(),
                Recipient =
                    string.IsNullOrWhiteSpace(
                        recipient)
                        ? null
                        : recipient.Trim(),
                Text =
                    (text ?? string.Empty)
                    .Trim(),
                IsOutgoing = isOutgoing
            };
        }

        private static string NormalizeInstantMessengerChannelName(
            string channel)
        {
            string normalized =
                string.IsNullOrWhiteSpace(
                    channel)
                    ? "Other"
                    : channel.Trim();

            normalized =
                Regex.Replace(
                    normalized,
                    @":\d+$",
                    string.Empty)
                .Trim();

            string compact =
                new(
                    normalized
                    .Where(char.IsLetterOrDigit)
                    .ToArray());

            if (compact.Equals(
                    "newplayers",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "newplayer",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "New Players";
            }

            if (compact.Equals(
                    "group",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "groupsay",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "party",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "yourparty",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Group";
            }

            if (compact.Equals(
                    "tell",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "tells",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Tells";
            }

            if (compact.Equals(
                    "ooc",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "outofcharacter",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "OOC";
            }

            return normalized;
        }

        private static InstantMessengerChannelPreference
            CreateDefaultInstantMessengerChannelPreference(
                string channel)
        {
            string normalized =
                NormalizeInstantMessengerChannelName(
                    channel);

            return new InstantMessengerChannelPreference
            {
                AutoMarkRead =
                    normalized.Equals(
                        "General",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        "New Players",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        "Say",
                        StringComparison.OrdinalIgnoreCase),
                AutoMarkImportant =
                    normalized.Equals(
                        "Guild",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        "Group",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        "Tells",
                        StringComparison.OrdinalIgnoreCase),
                MarkMyNameImportant = true,
                IgnoreAll = false
            };
        }

        private int LoadDebugInstantMessengerSnapshot(
            string logPath)
        {
            if (!_instantMessengerEnabled)
            {
                return 0;
            }

            InitializeInstantMessengerForCurrentCharacter();

            Queue<InstantMessageRecord> chatMessages =
                new();

            using FileStream stream =
                OpenSharedRead(
                    logPath);
            using StreamReader reader =
                new(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 16 * 1024,
                    leaveOpen: false);

            while (reader.ReadLine() is string line)
            {
                if (!TryGetMessage(
                        line,
                        out DateTime timestamp,
                        out string message))
                {
                    continue;
                }

                Match petMatch =
                    PetAttackRegex.Match(
                        message);

                if (petMatch.Success)
                {
                    string petName =
                        NormalizeEntity(
                            petMatch.Groups["pet"].Value);

                    if (!string.IsNullOrWhiteSpace(
                            petName))
                    {
                        _petOwners[petName] =
                            _characterName;
                    }
                }

                if (!TryParseInstantMessengerMessage(
                        message,
                        timestamp,
                        out InstantMessageRecord? chatMessage))
                {
                    continue;
                }

                chatMessages.Enqueue(
                    chatMessage!);

                while (chatMessages.Count >
                       DebugMaximumChatMessages)
                {
                    chatMessages.Dequeue();
                }
            }

            List<InstantMessageRecord> replayMessages =
                chatMessages.ToList();

            _instantMessengerWindow?
                .ReplaceWithDebugMessages(
                    replayMessages);

            return replayMessages.Count;
        }


        private void LoadDebugSnapshotFromLatestLog()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    SetStatus(
                        $"DEBUG — Log folder not found: {_logDirectory}");
                    return;
                }

                FileInfo? newest = Directory
                    .EnumerateFiles(
                        _logDirectory,
                        "eqlog_*_*.txt",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(info =>
                        FileNameRegex.IsMatch(info.Name))
                    .OrderByDescending(info =>
                        info.LastWriteTimeUtc)
                    .ThenByDescending(info =>
                        info.Length)
                    .FirstOrDefault();

                if (newest == null)
                {
                    SetStatus(
                        "DEBUG — No eqlog_CHARACTER_SERVER.txt file was found.");
                    return;
                }

                Match fileMatch =
                    FileNameRegex.Match(newest.Name);
                if (!fileMatch.Success)
                {
                    SetStatus(
                        $"DEBUG — The newest log name was not recognized: {newest.Name}");
                    return;
                }

                _activeFilePath = newest.FullName;
                _characterName =
                    fileMatch.Groups["character"].Value;
                _serverName =
                    CapitalizeFirst(
                        fileMatch.Groups["server"].Value);

                InitializeInstantMessengerForCurrentCharacter();
                ResetAllState();

                TailReadResult tail =
                    ReadLastLinesShared(
                        newest.FullName,
                        DebugMaximumLogLines);

                _fileOffset = tail.FileLength;
                _lastWriteTimeUtc = newest.LastWriteTimeUtc;
                _pendingText = string.Empty;

                List<string> replayLines = tail.Lines
                    .Where(line =>
                        TryGetMessage(
                            line,
                            out _,
                            out _))
                    .ToList();

                if (replayLines.Count == 0)
                {
                    RefreshDisplay();
                    SetStatus(
                        $"DEBUG — No parseable lines were found in {newest.Name}.");
                    return;
                }

                DateTime replayEnd =
                    DateTime.Now.AddMilliseconds(-100);

                double secondsPerLine =
                    replayLines.Count <= 1
                        ? 0
                        : Math.Min(
                            DebugMaximumSecondsPerLine,
                            DebugReplayWindowSeconds /
                            (replayLines.Count - 1));

                DateTime replayStart =
                    replayEnd.AddSeconds(
                        -secondsPerLine *
                        Math.Max(
                            0,
                            replayLines.Count - 1));

                _isLoadingDebugSnapshot = true;

                try
                {
                    for (int index = 0;
                         index < replayLines.Count;
                         index++)
                    {
                        DateTime syntheticTimestamp =
                            replayStart.AddSeconds(
                                secondsPerLine * index);

                        ProcessRawLine(
                            replayLines[index],
                            isInitialLoad: true,
                            timestampOverride:
                                syntheticTimestamp);
                    }
                }
                finally
                {
                    _isLoadingDebugSnapshot = false;
                }

                _pendingBarrierTimestamp = null;
                _pendingBarrierWallClock = null;
                _fightEnd = null;
                _latestLogTimestamp = replayEnd;

                if (_fightDamageEvents.Count > 0)
                {
                    _fightStart =
                        _fightDamageEvents.Min(
                            entry => entry.Timestamp);
                    _lastCombatActivity =
                        _fightDamageEvents.Max(
                            entry => entry.Timestamp);
                    _fightActive = true;
                }

                if (!_sessionStart.HasValue)
                {
                    _sessionStart = replayStart;
                }

                PruneOldDamageEvents();
                RefreshDisplay();

                int debugChatMessageCount =
                    LoadDebugInstantMessengerSnapshot(
                        newest.FullName);

                string fightState =
                    _fightActive
                        ? "Combat active"
                        : _fightStart.HasValue
                            ? "Last fight complete"
                            : "Waiting for damage";

                SetStatus(
                    $"DEBUG snapshot — {fightState} — loaded " +
                    $"{replayLines.Count:N0} combat-source lines and " +
                    $"{debugChatMessageCount:N0} chat messages from {newest.Name}");
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"DEBUG — Unable to load the snapshot: {ex.Message}");
            }
        }


        private void DetectLatestLogFile()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    SetStatus($"Log folder not found: {_logDirectory}");
                    return;
                }

                FileInfo? newest = Directory
                    .EnumerateFiles(_logDirectory, "eqlog_*_*.txt", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(info => FileNameRegex.IsMatch(info.Name))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .ThenByDescending(info => info.Length)
                    .FirstOrDefault();

                if (newest == null)
                {
                    SetStatus("No eqlog_CHARACTER_SERVER.txt file was found.");
                    return;
                }

                if (!string.Equals(
                        newest.FullName,
                        _activeFilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    SwitchToLogFile(newest.FullName);
                    return;
                }

                if (newest.Length < _fileOffset)
                {
                    // The game or another process truncated/replaced the active log.
                    SwitchToLogFile(newest.FullName);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Unable to scan the log folder: {ex.Message}");
            }
        }

        private void SwitchToLogFile(string path)
        {
            try
            {
                Match fileMatch = FileNameRegex.Match(Path.GetFileName(path));
                if (!fileMatch.Success)
                {
                    return;
                }

                _activeFilePath = path;
                _characterName = fileMatch.Groups["character"].Value;
                _serverName = CapitalizeFirst(fileMatch.Groups["server"].Value);

                InitializeInstantMessengerForCurrentCharacter();
                ResetAllState();

                int maximumLinesPerRead =
                    GetCurrentMaximumLinesPerRead();

                TailReadResult tail =
                    ReadLastLinesShared(
                        path,
                        maximumLinesPerRead);
                _fileOffset = tail.FileLength;
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);

                int latestWelcomeIndex = -1;
                for (int i = 0; i < tail.Lines.Count; i++)
                {
                    if (TryGetMessage(tail.Lines[i], out _, out string message) &&
                        message.Equals(
                            "Welcome to EverQuest Legends!",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        latestWelcomeIndex = i;
                    }
                }

                int startIndex = latestWelcomeIndex >= 0 ? latestWelcomeIndex : 0;
                for (int i = startIndex; i < tail.Lines.Count; i++)
                {
                    ProcessRawLine(tail.Lines[i], isInitialLoad: true);
                }

                if (_sessionStart == null &&
                    tail.Lines.Skip(startIndex)
                        .Select(line => TryGetMessage(line, out DateTime time, out _) ? time : (DateTime?)null)
                        .FirstOrDefault(time => time.HasValue) is DateTime firstTime)
                {
                    _sessionStart = firstTime;
                }

                ResolveInitialBarrierState();
                PruneOldDamageEvents();

                SetStatus($"Monitoring {Path.GetFileName(path)}");
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                SetStatus($"Unable to open the newest log: {ex.Message}");
            }
        }

        private void ReadAppendedLogData()
        {
            if (string.IsNullOrWhiteSpace(_activeFilePath))
            {
                return;
            }

            try
            {
                if (!File.Exists(_activeFilePath))
                {
                    SetStatus("The active log disappeared. Searching again...");
                    DetectLatestLogFile();
                    return;
                }

                FileInfo info = new(_activeFilePath);
                if (info.Length < _fileOffset)
                {
                    SwitchToLogFile(_activeFilePath);
                    return;
                }

                if (info.Length == _fileOffset &&
                    info.LastWriteTimeUtc == _lastWriteTimeUtc)
                {
                    return;
                }

                if (info.Length == _fileOffset)
                {
                    _lastWriteTimeUtc = info.LastWriteTimeUtc;
                    return;
                }

                long bytesAvailable = info.Length - _fileOffset;
                if (bytesAvailable > int.MaxValue)
                {
                    // This should never happen during a normal polling interval.
                    // Recover by reopening the log and loading only the current
                    // refresh-rate line cap instead of allocating gigabytes.
                    SwitchToLogFile(_activeFilePath);
                    return;
                }

                byte[] buffer = new byte[(int)bytesAvailable];
                int totalRead = 0;

                using (FileStream stream = OpenSharedRead(_activeFilePath))
                {
                    stream.Seek(_fileOffset, SeekOrigin.Begin);

                    while (totalRead < buffer.Length)
                    {
                        int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                        if (read <= 0)
                        {
                            break;
                        }

                        totalRead += read;
                    }
                }

                _fileOffset += totalRead;
                _lastWriteTimeUtc = info.LastWriteTimeUtc;
                if (totalRead <= 0)
                {
                    return;
                }

                string appended = Encoding.UTF8.GetString(buffer, 0, totalRead);
                string combined = _pendingText + appended;
                combined = combined.Replace("\r\n", "\n").Replace('\r', '\n');

                string[] pieces = combined.Split('\n');
                bool endsWithNewLine = combined.EndsWith("\n", StringComparison.Ordinal);

                int completeCount = endsWithNewLine ? pieces.Length : pieces.Length - 1;
                _pendingText = endsWithNewLine ? string.Empty : pieces[^1];

                List<string> completeLines = pieces
                    .Take(Math.Max(0, completeCount))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                if (_instantMessengerEnabled)
                {
                    foreach (string line in completeLines)
                    {
                        if (!TryGetMessage(
                                line,
                                out DateTime chatTimestamp,
                                out string chatMessage))
                        {
                            continue;
                        }

                        Match chatPetMatch =
                            PetAttackRegex.Match(
                                chatMessage);

                        if (chatPetMatch.Success)
                        {
                            string petName =
                                NormalizeEntity(
                                    chatPetMatch.Groups["pet"].Value);

                            if (!string.IsNullOrWhiteSpace(
                                    petName))
                            {
                                _petOwners[petName] =
                                    _characterName;
                            }
                        }

                        CaptureInstantMessengerMessage(
                            chatMessage,
                            chatTimestamp);
                    }
                }

                int maximumLinesPerRead =
                    GetCurrentMaximumLinesPerRead();

                List<string> lines = completeLines
                    .TakeLast(maximumLinesPerRead)
                    .ToList();

                foreach (string line in lines)
                {
                    ProcessRawLine(
                        line,
                        isInitialLoad: false,
                        captureInstantMessenger: false);
                }

                SetStatus($"Monitoring {Path.GetFileName(_activeFilePath)}");
            }
            catch (IOException)
            {
                // EverQuest may briefly have the file between writes. Retry next second.
            }
            catch (UnauthorizedAccessException ex)
            {
                SetStatus($"Cannot read the log: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetStatus($"Log read error: {ex.Message}");
            }
        }

        private void ProcessRawLine(
            string rawLine,
            bool isInitialLoad,
            DateTime? timestampOverride = null,
            bool captureInstantMessenger = true)
        {
            if (!TryGetMessage(
                    rawLine,
                    out DateTime parsedTimestamp,
                    out string message))
            {
                return;
            }

            DateTime timestamp =
                timestampOverride ??
                parsedTimestamp;

            if (_instantMessengerEnabled &&
                !isInitialLoad &&
                captureInstantMessenger)
            {
                CaptureInstantMessengerMessage(
                    message,
                    timestamp);
            }

            if (_latestLogTimestamp == null || timestamp > _latestLogTimestamp)
            {
                _latestLogTimestamp = timestamp;
            }

            _recentLogLines.Enqueue(new CachedLogLine(timestamp, rawLine));
            while (_recentLogLines.Count > MaximumRetainedLogLines)
            {
                _recentLogLines.Dequeue();
            }

            TryFinalizePendingBarrierByLogTime(timestamp);

            if (_pendingPartyExperienceTimestamp.HasValue &&
                timestamp >
                _pendingPartyExperienceTimestamp.Value.AddSeconds(FightBarrierSeconds))
            {
                _pendingPartyExperienceTimestamp = null;
            }

            if (message.Equals(
                    "Welcome to EverQuest Legends!",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_isLoadingDebugSnapshot)
                {
                    _sessionStart ??= timestamp;
                    return;
                }

                ResetForNewLogin(timestamp);
                return;
            }

            if (TryHandleGroupMessage(message))
            {
                return;
            }

            if (TryHandleSpecialCombatMessage(
                    message,
                    timestamp,
                    isInitialLoad))
            {
                return;
            }

            Match petMatch = PetAttackRegex.Match(message);
            if (petMatch.Success)
            {
                string pet =
                    NormalizeEntity(
                        petMatch.Groups["pet"].Value);
                string target =
                    NormalizeEntity(
                        petMatch.Groups["target"].Value);

                if (!string.IsNullOrWhiteSpace(pet))
                {
                    _petOwners[pet] = _characterName;

                    if (_dataFilterMode ==
                        DataFilterMode.OnlyKnowns)
                    {
                        RebuildOnlyKnownCombatScope();

                        if (_fightActive)
                        {
                            RegisterOnlyKnownCombatInteraction(
                                pet,
                                target);
                        }
                    }
                }

                return;
            }

            if (TryParseCurrency(message, out long copperValue))
            {
                _currencyEvents.Add(
                    new CurrencyEvent(timestamp, copperValue));

                PruneCurrencyEvents(timestamp);
                return;
            }

            Match experienceMatch = ExperienceRegex.Match(message);
            if (experienceMatch.Success &&
                double.TryParse(
                    experienceMatch.Groups["percent"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double experiencePercent))
            {
                if (_sessionStart == null)
                {
                    _sessionStart = timestamp;
                }

                _experienceEvents.Add(new ExperienceEvent(timestamp, experiencePercent));

                if (experienceMatch.Groups["party"].Success)
                {
                    _pendingPartyExperienceTimestamp = timestamp;
                }

                return;
            }

            if (TryParseKill(message, out string killedEntity, out string killer))
            {
                killer = NormalizeEntity(killer);
                killedEntity = NormalizeEntity(killedEntity);

                if (_pendingPartyExperienceTimestamp.HasValue &&
                    timestamp >= _pendingPartyExperienceTimestamp.Value &&
                    timestamp <=
                    _pendingPartyExperienceTimestamp.Value.AddSeconds(FightBarrierSeconds))
                {
                    if (!IsSelf(killer) && !IsOwnedPet(killer))
                    {
                        AddGroupMember(killer);
                    }

                    _pendingPartyExperienceTimestamp = null;
                }

                if (!ShouldProcessKillForCurrentFilter(
                        killedEntity,
                        killer))
                {
                    return;
                }

                if (IsProtectedFriendlyEntity(killer))
                {
                    _knownBadGuys.Add(killedEntity);
                }

                if (_fightActive &&
                    !_isLoadingDebugSnapshot)
                {
                    _pendingBarrierTimestamp = timestamp;
                    _pendingBarrierWallClock =
                        isInitialLoad
                            ? null
                            : DateTime.Now;
                }

                return;
            }

            if (TryParseDamage(message, timestamp, out DamageEvent? damageEvent))
            {
                HandleDamageEvent(damageEvent!);
                return;
            }

            Match avoidMatch = AvoidRegex.Match(message);
            if (avoidMatch.Success)
            {
                string source = NormalizeEntity(
                    avoidMatch.Groups["source"].Value);
                string target = NormalizeEntity(
                    avoidMatch.Groups["target"].Value);

                HandleAvoidEvent(timestamp, source, target);
            }
        }

        private void HandleDamageEvent(DamageEvent damageEvent)
        {
            bool directlyInvolvesProtectedEntity =
                IsProtectedFriendlyEntity(
                    damageEvent.Source) ||
                IsProtectedFriendlyEntity(
                    damageEvent.Target);

            if (!ShouldAcceptDamageEventForCurrentFilter(
                    damageEvent))
            {
                return;
            }

            MarkHostileIfThreatensProtectedEntity(
                damageEvent.Source,
                damageEvent.Target);

            if (_pendingBarrierTimestamp.HasValue)
            {
                if (damageEvent.Timestamp <=
                    _pendingBarrierTimestamp.Value.AddSeconds(FightBarrierSeconds))
                {
                    _pendingBarrierTimestamp = null;
                    _pendingBarrierWallClock = null;
                }
                else
                {
                    FinalizeFight(_pendingBarrierTimestamp.Value);
                }
            }

            if (!_fightActive)
            {
                if (_dataFilterMode ==
                        DataFilterMode.OnlyKnowns &&
                    !directlyInvolvesProtectedEntity)
                {
                    return;
                }

                StartNewFight(damageEvent.Timestamp);
            }

            RegisterOnlyKnownCombatInteraction(
                damageEvent.Source,
                damageEvent.Target);

            if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns &&
                (!IsEntityInOnlyKnownCombatScope(
                     damageEvent.Source) ||
                 !IsEntityInOnlyKnownCombatScope(
                     damageEvent.Target)))
            {
                return;
            }

            _lastCombatActivity = damageEvent.Timestamp;
            _fightDamageEvents.Add(damageEvent);
            _latestTargets[damageEvent.Source] =
                new TargetEvent(
                    damageEvent.Timestamp,
                    damageEvent.Target);
            PruneOldDamageEvents();
        }

        private void HandleAvoidEvent(
            DateTime timestamp,
            string source,
            string target)
        {
            if (!_fightActive)
            {
                // Misses do not begin a new encounter. They only keep an existing
                // encounter's DPS timer alive, as requested.
                return;
            }

            if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns &&
                !IsDirectProtectedCombatInteraction(
                    source,
                    target) &&
                (!IsEntityInOnlyKnownCombatScope(source) ||
                 !IsEntityInOnlyKnownCombatScope(target)))
            {
                return;
            }

            if (_pendingBarrierTimestamp.HasValue)
            {
                if (timestamp <=
                    _pendingBarrierTimestamp.Value.AddSeconds(FightBarrierSeconds))
                {
                    _pendingBarrierTimestamp = null;
                    _pendingBarrierWallClock = null;
                }
                else
                {
                    FinalizeFight(_pendingBarrierTimestamp.Value);
                    return;
                }
            }

            RegisterOnlyKnownCombatInteraction(
                source,
                target);
            MarkHostileIfThreatensProtectedEntity(
                source,
                target);

            if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns &&
                (!IsEntityInOnlyKnownCombatScope(source) ||
                 !IsEntityInOnlyKnownCombatScope(target)))
            {
                return;
            }

            _lastCombatActivity = timestamp;

            if (!string.IsNullOrWhiteSpace(source) &&
                !string.IsNullOrWhiteSpace(target))
            {
                _latestTargets[source] =
                    new TargetEvent(timestamp, target);
            }
        }

        private void StartNewFight(DateTime timestamp)
        {
            _fightDamageEvents.Clear();
            _latestTargets.Clear();
            _onlyKnownCombatEntities.Clear();
            _fightStart = timestamp;
            _fightEnd = null;
            _lastCombatActivity = timestamp;
            _fightActive = true;
            _pendingBarrierTimestamp = null;
            _pendingBarrierWallClock = null;
        }

        private void FinalizeFight(DateTime barrierTimestamp)
        {
            if (!_fightActive)
            {
                _pendingBarrierTimestamp = null;
                _pendingBarrierWallClock = null;
                return;
            }

            _fightActive = false;
            _fightEnd = barrierTimestamp;
            _pendingBarrierTimestamp = null;
            _pendingBarrierWallClock = null;
            PruneOldDamageEvents();
        }

        private void TryFinalizePendingBarrierByLogTime(DateTime nextLineTimestamp)
        {
            if (_pendingBarrierTimestamp.HasValue &&
                nextLineTimestamp >=
                _pendingBarrierTimestamp.Value.AddSeconds(FightBarrierSeconds))
            {
                FinalizeFight(_pendingBarrierTimestamp.Value);
            }
        }

        private void TryFinalizePendingBarrierByWallClock()
        {
            if (!_pendingBarrierTimestamp.HasValue || !_fightActive)
            {
                return;
            }

            if (_pendingBarrierWallClock.HasValue &&
                DateTime.Now - _pendingBarrierWallClock.Value >=
                TimeSpan.FromSeconds(FightBarrierSeconds))
            {
                FinalizeFight(_pendingBarrierTimestamp.Value);
            }
        }

        private void ResolveInitialBarrierState()
        {
            if (!_pendingBarrierTimestamp.HasValue)
            {
                return;
            }

            if (_latestLogTimestamp.HasValue &&
                _latestLogTimestamp.Value >=
                _pendingBarrierTimestamp.Value.AddSeconds(FightBarrierSeconds))
            {
                FinalizeFight(_pendingBarrierTimestamp.Value);
                return;
            }

            TimeSpan age = DateTime.Now - _pendingBarrierTimestamp.Value;
            if (age >= TimeSpan.FromSeconds(FightBarrierSeconds))
            {
                FinalizeFight(_pendingBarrierTimestamp.Value);
            }
            else
            {
                _pendingBarrierWallClock =
                    DateTime.Now - TimeSpan.FromSeconds(Math.Max(0, age.TotalSeconds));
            }
        }

        private bool TryHandleGroupMessage(string message)
        {
            Match match = GroupInviteRegex.Match(message);
            if (match.Success)
            {
                _pendingGroupInviter =
                    NormalizeEntity(match.Groups["member"].Value);
                return true;
            }

            match = GroupAcceptRegex.Match(message);
            if (match.Success)
            {
                _pendingGroupInviter =
                    NormalizeEntity(match.Groups["member"].Value);
                return true;
            }

            if (message.Equals(
                    "You have joined the group.",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(_pendingGroupInviter))
                {
                    AddGroupMember(_pendingGroupInviter);
                }

                _pendingGroupInviter = null;
                return true;
            }

            if (GroupClearedRegex.IsMatch(message))
            {
                _groupMembers.Clear();
                _pendingGroupInviter = null;
                _pendingPartyExperienceTimestamp = null;

                if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns)
                {
                    RebuildOnlyKnownCombatScope();
                }

                return true;
            }

            match = GroupMemberJoinedRegex.Match(message);
            if (match.Success)
            {
                AddGroupMember(match.Groups["member"].Value);
                return true;
            }

            match = GroupMemberLeftRegex.Match(message);
            if (match.Success)
            {
                string member = NormalizeEntity(match.Groups["member"].Value);
                _groupMembers.Remove(member);

                if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns)
                {
                    RebuildOnlyKnownCombatScope();
                }

                return true;
            }

            match = GroupChatRegex.Match(message);
            if (match.Success)
            {
                AddGroupMember(match.Groups["member"].Value);
                return true;
            }

            return false;
        }

        private void AddGroupMember(string rawMember)
        {
            string member = NormalizeEntity(rawMember);
            if (!string.IsNullOrWhiteSpace(member) && !IsSelf(member))
            {
                _groupMembers.Add(member);

                if (_dataFilterMode ==
                    DataFilterMode.OnlyKnowns)
                {
                    RebuildOnlyKnownCombatScope();
                }
            }
        }

        private bool TryHandleSpecialCombatMessage(
            string message,
            DateTime logTimestamp,
            bool isInitialLoad)
        {
            if (TryParseSpellCast(message, out ParsedSpellCast? spellCast))
            {
                if (!isInitialLoad)
                {
                    HandleSpellCastVisual(spellCast!, logTimestamp);
                }

                return true;
            }

            if (TryParseHealEvent(message, out ParsedHealEvent? healEvent))
            {
                if (!string.IsNullOrWhiteSpace(healEvent.Spell))
                {
                    _learnedHealingSpellNames.Add(
                        NormalizeSpellNameForClassification(
                            healEvent.Spell));
                }

                if (!isInitialLoad)
                {
                    HandleHealVisual(healEvent!, logTimestamp);
                }

                return true;
            }

            Match receivedSpellMatch = ReceivedSpellRegex.Match(message);
            if (receivedSpellMatch.Success)
            {
                string spellName =
                    receivedSpellMatch.Groups["spell"].Value;
                string target = NormalizeVisualEntity(
                    receivedSpellMatch.Groups["target"].Value,
                    null);

                bool isDamageSpell =
                    TryClassifyDamageSpell(
                        spellName,
                        out _,
                        out _);

                bool isCrowdControlSpell =
                    TryClassifyCrowdControlSpell(
                        spellName,
                        out CrowdControlType type,
                        out bool isAreaEffect);

                if (isDamageSpell || isCrowdControlSpell)
                {
                    string? caster =
                        FindRecentCasterForSpell(
                            spellName,
                            target);

                    RegisterOnlyKnownCombatInteraction(
                        caster,
                        target);
                    MarkHostileIfThreatensProtectedEntity(
                        caster,
                        target);
                }

                if (isCrowdControlSpell)
                {
                    if (!isInitialLoad)
                    {
                        AddCrowdControlLandedIndicator(
                            target,
                            type,
                            isAreaEffect,
                            logTimestamp,
                            spellName);
                    }

                    return true;
                }

                if (isDamageSpell)
                {
                    return true;
                }
            }

            if (TryParseCrowdControlLanding(
                    message,
                    out string landedTarget,
                    out CrowdControlType landedType,
                    out bool? areaEffectHint))
            {
                if (!isInitialLoad)
                {
                    bool isAreaEffect =
                        ResolveRecentAreaEffectHint(
                            landedType,
                            areaEffectHint);

                    AddCrowdControlLandedIndicator(
                        landedTarget,
                        landedType,
                        isAreaEffect,
                        logTimestamp);
                }

                return true;
            }

            return false;
        }

        private bool TryParseSpellCast(
            string message,
            out ParsedSpellCast? spellCast)
        {
            Match match = SelfSpellCastRegex.Match(message);
            if (match.Success)
            {
                spellCast = new ParsedSpellCast(
                    _characterName,
                    CleanSpellName(match.Groups["spell"].Value));
                return true;
            }

            match = OtherSpellCastRegex.Match(message);
            if (match.Success)
            {
                spellCast = new ParsedSpellCast(
                    NormalizeVisualEntity(
                        match.Groups["caster"].Value,
                        null),
                    CleanSpellName(match.Groups["spell"].Value));
                return true;
            }

            match = OldSpellCastRegex.Match(message);
            if (match.Success)
            {
                spellCast = new ParsedSpellCast(
                    NormalizeVisualEntity(
                        match.Groups["caster"].Value,
                        null),
                    CleanSpellName(match.Groups["spell"].Value));
                return true;
            }

            match = SpecialAbilityRegex.Match(message);
            if (match.Success)
            {
                spellCast = new ParsedSpellCast(
                    NormalizeVisualEntity(
                        match.Groups["caster"].Value,
                        null),
                    CleanSpellName(match.Groups["spell"].Value));
                return true;
            }

            spellCast = null;
            return false;
        }

        private bool TryParseHealEvent(
            string message,
            out ParsedHealEvent? healEvent)
        {
            Match match = DirectHealRegex.Match(message);
            if (match.Success)
            {
                string healer = NormalizeVisualEntity(
                    match.Groups["healer"].Value,
                    null);

                string target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    healer);

                healEvent = new ParsedHealEvent(
                    healer,
                    target,
                    CleanSpellName(
                        match.Groups["spell"].Value));

                return true;
            }

            match = PassiveHealRegex.Match(message);
            if (match.Success)
            {
                string target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);

                healEvent = new ParsedHealEvent(
                    null,
                    target,
                    CleanSpellName(
                        match.Groups["spell"].Value));

                return true;
            }

            match = LegacyHealRegex.Match(message);
            if (match.Success)
            {
                string? healer =
                    match.Groups["healer"].Success
                        ? NormalizeVisualEntity(
                            match.Groups["healer"].Value,
                            null)
                        : null;

                string target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    healer);

                healEvent = new ParsedHealEvent(
                    healer,
                    target,
                    string.Empty);

                return true;
            }

            healEvent = null;
            return false;
        }

        private void HandleSpellCastVisual(
            ParsedSpellCast spellCast,
            DateTime logTimestamp)
        {
            string caster = spellCast.Caster;
            string spellName = spellCast.Spell;

            if (string.IsNullOrWhiteSpace(caster) ||
                string.IsNullOrWhiteSpace(spellName))
            {
                return;
            }

            TrackRecentSpellCastSource(
                caster,
                spellName);
            TrackLatestSpellCast(caster, spellName);

            if (IsTeleportationSpell(spellName))
            {
                AddSpecialIndicator(
                    caster,
                    "◆",
                    TeleportationSpellIndicatorBrush,
                    $"Casting teleportation: {spellName}",
                    TeleportationSpellCastIndicatorSeconds,
                    logTimestamp);
                return;
            }

            if (IsLayOnHandsSpell(spellName))
            {
                _recentLayOnHandsCasts.Add(
                    new RecentLayOnHandsCast(
                        caster,
                        DateTime.Now.AddSeconds(4)));

                if (IsHostileEntity(caster))
                {
                    string correlationKey =
                        BuildLayOnHandsCorrelationKey(
                            caster,
                            caster);

                    AddSpecialIndicator(
                        caster,
                        "<<",
                        HealingIndicatorBrush,
                        "Lay on Hands received",
                        LayOnHandsRecipientSeconds,
                        logTimestamp,
                        correlationKey,
                        dedupeSeconds: 2);

                    ExtendHealingAnimation(
                        caster,
                        LayOnHandsRecipientSeconds);
                }

                return;
            }

            if (IsHealingSpellName(spellName))
            {
                AddSpecialIndicator(
                    caster,
                    ">",
                    HealingIndicatorBrush,
                    "Casting a healing spell",
                    HealingCastIndicatorSeconds,
                    logTimestamp);
            }

            if (TryClassifyDamageSpell(
                    spellName,
                    out DamageSpellCastType damageSpellType,
                    out bool isAreaEffectDamage))
            {
                bool isDamageOverTime =
                    damageSpellType ==
                    DamageSpellCastType.DamageOverTime;

                string damageGlyph =
                    GetDamageSpellCastGlyph(
                        damageSpellType,
                        isAreaEffectDamage);

                string damageScope =
                    isAreaEffectDamage
                        ? "area effect "
                        : string.Empty;

                AddSpecialIndicator(
                    caster,
                    damageGlyph,
                    DamageSpellIndicatorBrush,
                    isDamageOverTime
                        ? $"Casting {damageScope}damage over time: {spellName}"
                        : $"Casting {damageScope}direct damage: {spellName}",
                    DamageSpellCastIndicatorSeconds,
                    logTimestamp,
                    BuildDamageSpellCastCorrelationKey(
                        caster,
                        spellName));
            }

            if (!TryClassifyCrowdControlSpell(
                    spellName,
                    out CrowdControlType type,
                    out bool isAreaEffect))
            {
                return;
            }

            AddSpecialIndicator(
                caster,
                GetCrowdControlGlyph(type, isAreaEffect),
                GetCrowdControlBrush(type),
                $"{GetCrowdControlLabel(type)} cast",
                CrowdControlCastIndicatorSeconds,
                logTimestamp);

            _recentCrowdControlCasts.Add(
                new RecentCrowdControlCast(
                    type,
                    isAreaEffect,
                    DateTime.Now.AddSeconds(
                        RecentCrowdControlCastSeconds)));
        }

        private void TrackRecentSpellCastSource(
            string caster,
            string spellName)
        {
            caster = NormalizeVisualEntity(caster, null);
            spellName = CleanSpellName(spellName);

            if (string.IsNullOrWhiteSpace(caster) ||
                string.IsNullOrWhiteSpace(spellName))
            {
                return;
            }

            _recentSpellCastSources.Add(
                new RecentSpellCastSource(
                    caster,
                    spellName,
                    DateTime.Now.AddSeconds(
                        HostileSpellCorrelationSeconds)));
        }

        private string? FindRecentCasterForSpell(
            string spellName,
            string affectedTarget)
        {
            string normalizedSpell =
                NormalizeSpellNameForClassification(
                    spellName);
            DateTime now = DateTime.Now;

            return _recentSpellCastSources
                .Where(entry =>
                    entry.ExpiresAt > now &&
                    NormalizeSpellNameForClassification(
                        entry.Spell)
                    .Equals(
                        normalizedSpell,
                        StringComparison.OrdinalIgnoreCase))
                .Where(entry =>
                    !IsProtectedFriendlyEntity(
                        entry.Caster) ||
                    !IsProtectedFriendlyEntity(
                        affectedTarget))
                .OrderByDescending(entry => entry.ExpiresAt)
                .Select(entry => entry.Caster)
                .FirstOrDefault();
        }

        private string? FindRecentCrowdControlCaster(
            CrowdControlType type,
            string affectedTarget)
        {
            DateTime now = DateTime.Now;

            return _recentSpellCastSources
                .Where(entry => entry.ExpiresAt > now)
                .Where(entry =>
                    TryClassifyCrowdControlSpell(
                        entry.Spell,
                        out CrowdControlType recentType,
                        out _) &&
                    recentType == type)
                .Where(entry =>
                    !IsProtectedFriendlyEntity(
                        entry.Caster) ||
                    !IsProtectedFriendlyEntity(
                        affectedTarget))
                .OrderByDescending(entry => entry.ExpiresAt)
                .Select(entry => entry.Caster)
                .FirstOrDefault();
        }

        private void TrackLatestSpellCast(
            string caster,
            string spellName)
        {
            if (!_showSpellCastingSubtext)
            {
                return;
            }

            caster = NormalizeVisualEntity(caster, null);
            spellName = CleanSpellName(spellName);

            if (string.IsNullOrWhiteSpace(caster) ||
                string.IsNullOrWhiteSpace(spellName))
            {
                return;
            }

            _latestSpellCasts[caster] =
                new RecentSpellCast(
                    spellName,
                    GetSpellCastingSubtextBrush(spellName),
                    DateTime.Now.AddSeconds(
                        SpellCastingSubtextSeconds));
        }

        private global::System.Windows.Media.Brush GetSpellCastingSubtextBrush(
            string spellName)
        {
            if (IsTeleportationSpell(spellName))
            {
                return TeleportationSpellIndicatorBrush;
            }

            if (IsHealingSpellName(spellName))
            {
                return HealingIndicatorBrush;
            }

            if (TryClassifyCrowdControlSpell(
                    spellName,
                    out CrowdControlType crowdControlType,
                    out _))
            {
                return GetCrowdControlBrush(crowdControlType);
            }

            if (TryClassifyDamageSpell(
                    spellName,
                    out _,
                    out _))
            {
                return DamageSpellIndicatorBrush;
            }

            return UnclassifiedSpellIndicatorBrush;
        }

        private void HandleHealVisual(
            ParsedHealEvent healEvent,
            DateTime logTimestamp)
        {
            string target = healEvent.Target;
            string? healer = healEvent.Healer;
            string spellName = healEvent.Spell;

            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            bool spellIsLayOnHands =
                IsLayOnHandsSpell(spellName);

            string? matchedLayOnHandsCaster =
                TryConsumeRecentLayOnHandsCaster(
                    healer,
                    allowUnknownHealer: spellIsLayOnHands);

            if (string.IsNullOrWhiteSpace(healer) &&
                !string.IsNullOrWhiteSpace(
                    matchedLayOnHandsCaster))
            {
                healer = matchedLayOnHandsCaster;
            }

            bool isLayOnHands =
                spellIsLayOnHands ||
                !string.IsNullOrWhiteSpace(
                    matchedLayOnHandsCaster);

            if (isLayOnHands)
            {
                bool targetIsRelevant =
                    IsSelf(target) ||
                    IsGroupMember(target) ||
                    (!string.IsNullOrWhiteSpace(healer) &&
                     target.Equals(
                         healer,
                         StringComparison.OrdinalIgnoreCase));

                if (targetIsRelevant)
                {
                    string correlationKey =
                        BuildLayOnHandsCorrelationKey(
                            healer,
                            target);

                    AddSpecialIndicator(
                        target,
                        "<<",
                        HealingIndicatorBrush,
                        "Lay on Hands received",
                        LayOnHandsRecipientSeconds,
                        logTimestamp,
                        correlationKey,
                        dedupeSeconds: 2);

                    ExtendHealingAnimation(
                        target,
                        LayOnHandsRecipientSeconds);
                }

                if (!string.IsNullOrWhiteSpace(healer) &&
                    !healer.Equals(
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddSpecialIndicator(
                        healer,
                        ">>",
                        HealingIndicatorBrush,
                        $"Cast Lay on Hands on {target}",
                        LayOnHandsCasterSeconds,
                        logTimestamp);
                }

                return;
            }

            if (IsSelf(target) || IsGroupMember(target))
            {
                AddSpecialIndicator(
                    target,
                    "<",
                    HealingIndicatorBrush,
                    string.IsNullOrWhiteSpace(spellName)
                        ? "Healing received"
                        : $"Hit by {spellName}",
                    HealingReceivedIndicatorSeconds,
                    logTimestamp);
            }
        }

        private string? TryConsumeRecentLayOnHandsCaster(
            string? healer,
            bool allowUnknownHealer)
        {
            DateTime now = DateTime.Now;

            int index = -1;

            if (!string.IsNullOrWhiteSpace(healer))
            {
                index = _recentLayOnHandsCasts.FindLastIndex(
                    entry =>
                        entry.ExpiresAt > now &&
                        entry.Caster.Equals(
                            healer,
                            StringComparison.OrdinalIgnoreCase));
            }
            else if (allowUnknownHealer)
            {
                index = _recentLayOnHandsCasts.FindLastIndex(
                    entry => entry.ExpiresAt > now);
            }

            if (index < 0)
            {
                return null;
            }

            string caster =
                _recentLayOnHandsCasts[index].Caster;

            _recentLayOnHandsCasts.RemoveAt(index);
            return caster;
        }

        private void AddCrowdControlLandedIndicator(
            string target,
            CrowdControlType type,
            bool isAreaEffect,
            DateTime logTimestamp,
            string? spellName = null)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            string? caster =
                !string.IsNullOrWhiteSpace(spellName)
                    ? FindRecentCasterForSpell(
                        spellName,
                        target)
                    : FindRecentCrowdControlCaster(
                        type,
                        target);

            RegisterOnlyKnownCombatInteraction(
                caster,
                target);
            MarkHostileIfThreatensProtectedEntity(
                caster,
                target);

            AddSpecialIndicator(
                target,
                "X",
                GetCrowdControlBrush(type),
                $"{GetCrowdControlLabel(type)} landed",
                CrowdControlLandedIndicatorSeconds,
                logTimestamp);
        }

        private bool TryParseCrowdControlLanding(
            string message,
            out string target,
            out CrowdControlType type,
            out bool? areaEffectHint)
        {
            string normalized = message
                .Trim()
                .TrimEnd('.', '!')
                .Replace('’', '\'');

            if (normalized.Equals(
                    "You have been charmed",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You are charmed",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Charm;
                areaEffectHint = false;
                return true;
            }

            Match match = Regex.Match(
                normalized,
                @"^(?<target>.+?) (?:has been|is|becomes) charmed$",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Charm;
                areaEffectHint = false;
                return true;
            }

            if (normalized.Equals(
                    "Your feet become entwined",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "Your feet adhere to the ground",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You are entrapped by roots",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Root;
                areaEffectHint = false;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?)(?:'s)? feet become entwined$",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Root;
                areaEffectHint = false;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?) " +
                @"(?:is rooted|is entrapped by roots)$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                match = Regex.Match(
                    normalized,
                    @"^(?<target>.+?)(?:'s)? " +
                    @"(?:feet adhere to the ground|" +
                    @"feet are stuck to the ground)$",
                    RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Root;
                areaEffectHint = false;
                return true;
            }

            if (normalized.Equals(
                    "You feel your aggression subside",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You feel less aggressive",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Lull;
                areaEffectHint = false;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?) " +
                @"(?:looks less aggressive|is pacified|looks ambivalent)$",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Lull;
                areaEffectHint = false;
                return true;
            }

            if (normalized.Equals(
                    "You feel quite drowsy",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(
                    "You swoon",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Mesmerize;
                areaEffectHint = null;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?)(?:'s)? " +
                @"(?:eyes glaze over|head nods)$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                match = Regex.Match(
                    normalized,
                    @"^(?<target>.+?) " +
                    @"(?:stands around looking utterly confused|" +
                    @"swoons in raptured bliss|" +
                    @"falls into an enchanted sleep|" +
                    @"is mesmerized)$",
                    RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Mesmerize;
                areaEffectHint = null;
                return true;
            }

            if (normalized.Equals(
                    "You are stunned by scintillating colors",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Stun;
                areaEffectHint = true;
                return true;
            }

            if (normalized.Equals(
                    "You are stunned",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You are struck by a sudden force",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You have been struck by the force of Ykesha",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You are knocked backwards by a concussion of air",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "Reality runs amok",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You reel",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "You begin to spin",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _characterName;
                type = CrowdControlType.Stun;
                areaEffectHint = null;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?) " +
                @"(?:is stunned|" +
                @"has been stunned|" +
                @"is struck by a sudden force|" +
                @"has been struck by the force of Ykesha|" +
                @"is knocked backwards by a concussion of air|" +
                @"begins to spin|" +
                @"reels|" +
                @"looks delirious|" +
                @"is surrounded by fluxing strands of chaos)$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                match = Regex.Match(
                    normalized,
                    @"^(?<target>.+?)(?:'s)? world dissolves into anarchy$",
                    RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Stun;
                areaEffectHint = null;
                return true;
            }

            match = Regex.Match(
                normalized,
                @"^(?<target>.+?) is stunned by scintillating colors$",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                target = NormalizeVisualEntity(
                    match.Groups["target"].Value,
                    null);
                type = CrowdControlType.Stun;
                areaEffectHint = true;
                return true;
            }

            target = string.Empty;
            type = default;
            areaEffectHint = null;
            return false;
        }

        private bool ResolveRecentAreaEffectHint(
            CrowdControlType type,
            bool? explicitHint)
        {
            if (explicitHint.HasValue)
            {
                return explicitHint.Value;
            }

            DateTime now = DateTime.Now;

            RecentCrowdControlCast? recent =
                _recentCrowdControlCasts
                    .Where(entry =>
                        entry.ExpiresAt > now &&
                        entry.Type == type)
                    .LastOrDefault();

            return recent?.IsAreaEffect ?? false;
        }

        private static double CalculateIndicatorOpacity(
            SpecialIndicatorEvent indicator)
        {
            double remainingSeconds =
                (indicator.ExpiresAt - DateTime.Now)
                    .TotalSeconds;

            if (remainingSeconds <= 0)
            {
                return 0;
            }

            double totalSeconds = Math.Max(
                0.1,
                (indicator.ExpiresAt -
                 indicator.CreatedAt).TotalSeconds);

            double fadeSeconds = Math.Min(
                1.5,
                Math.Max(0.5, totalSeconds / 3.0));

            if (remainingSeconds >= fadeSeconds)
            {
                return 1;
            }

            return Math.Clamp(
                remainingSeconds / fadeSeconds,
                0.12,
                1.0);
        }

        private void AddSpecialIndicator(
            string entity,
            string glyph,
            global::System.Windows.Media.Brush foreground,
            string toolTip,
            int durationSeconds,
            DateTime logTimestamp,
            string? correlationKey = null,
            int dedupeSeconds = 0)
        {
            entity = NormalizeVisualEntity(entity, null);
            if (string.IsNullOrWhiteSpace(entity))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(correlationKey) &&
                dedupeSeconds > 0)
            {
                bool duplicate = _specialIndicatorEvents.Any(entry =>
                    string.Equals(
                        entry.CorrelationKey,
                        correlationKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(
                        (entry.LogTimestamp - logTimestamp)
                            .TotalSeconds) <= dedupeSeconds);

                if (duplicate)
                {
                    return;
                }
            }

            _specialIndicatorEvents.Add(
                new SpecialIndicatorEvent(
                    entity,
                    glyph,
                    foreground,
                    toolTip,
                    DateTime.Now.AddSeconds(durationSeconds),
                    logTimestamp,
                    correlationKey));
        }

        private void ExtendHealingAnimation(
            string entity,
            int durationSeconds)
        {
            entity = NormalizeVisualEntity(entity, null);
            if (string.IsNullOrWhiteSpace(entity))
            {
                return;
            }

            DateTime expiresAt =
                DateTime.Now.AddSeconds(durationSeconds);

            if (_healingAnimationUntil.TryGetValue(
                    entity,
                    out DateTime existing) &&
                existing > expiresAt)
            {
                return;
            }

            _healingAnimationUntil[entity] = expiresAt;
        }

        private void PruneSpecialVisualEvents()
        {
            DateTime now = DateTime.Now;

            _specialIndicatorEvents.RemoveAll(
                entry => entry.ExpiresAt <= now);

            _recentCrowdControlCasts.RemoveAll(
                entry => entry.ExpiresAt <= now);

            _recentLayOnHandsCasts.RemoveAll(
                entry => entry.ExpiresAt <= now);

            _recentSpellCastSources.RemoveAll(
                entry => entry.ExpiresAt <= now);

            List<string> expiredAnimations =
                _healingAnimationUntil
                    .Where(pair => pair.Value <= now)
                    .Select(pair => pair.Key)
                    .ToList();

            foreach (string entity in expiredAnimations)
            {
                _healingAnimationUntil.Remove(entity);
            }

            List<string> expiredSpellCasts =
                _latestSpellCasts
                    .Where(pair => pair.Value.ExpiresAt <= now)
                    .Select(pair => pair.Key)
                    .ToList();

            foreach (string entity in expiredSpellCasts)
            {
                _latestSpellCasts.Remove(entity);
            }
        }

        private void ClearSpecialVisualEvents()
        {
            _specialIndicatorEvents.Clear();
            _recentCrowdControlCasts.Clear();
            _recentLayOnHandsCasts.Clear();
            _recentSpellCastSources.Clear();
            _recentAreaDamageObservations.Clear();
            _healingAnimationUntil.Clear();
            _latestSpellCasts.Clear();
        }

        private bool TryClassifyDamageSpell(
            string spellName,
            out DamageSpellCastType damageSpellType,
            out bool isAreaEffect)
        {
            string normalized =
                NormalizeSpellNameForClassification(spellName);

            if (string.IsNullOrWhiteSpace(normalized) ||
                IsTeleportationSpell(normalized))
            {
                damageSpellType = default;
                isAreaEffect = false;
                return false;
            }

            isAreaEffect =
                AreaDamageSpellNames.Contains(normalized) ||
                _learnedAreaDamageSpellNames.Contains(normalized) ||
                IsRecognizedAreaDamageSpellPattern(normalized);

            if (DamageOverTimeSpellNames.Contains(normalized) ||
                _learnedDamageOverTimeSpellNames.Contains(normalized) ||
                IsRecognizedDamageOverTimeSpellPattern(normalized))
            {
                damageSpellType =
                    DamageSpellCastType.DamageOverTime;
                return true;
            }

            if (DirectDamageSpellNames.Contains(normalized) ||
                isAreaEffect ||
                _learnedDirectDamageSpellNames.Contains(normalized) ||
                IsRecognizedDirectDamageSpellPattern(normalized))
            {
                damageSpellType =
                    DamageSpellCastType.DirectDamage;
                return true;
            }

            damageSpellType = default;
            isAreaEffect = false;
            return false;
        }

        private static string GetDamageSpellCastGlyph(
            DamageSpellCastType damageSpellType,
            bool isAreaEffect)
        {
            if (isAreaEffect)
            {
                return damageSpellType ==
                       DamageSpellCastType.DamageOverTime
                    ? "□"
                    : "■";
            }

            return damageSpellType ==
                   DamageSpellCastType.DamageOverTime
                ? "○"
                : "●";
        }

        private static bool IsRecognizedAreaDamageSpellPattern(
            string normalized)
        {
            return normalized.StartsWith(
                       "rain of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "column of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "pillar of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "circle of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "tears of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "jyll's ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "chords of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "selo's chords of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(
                       " spiral of ",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecognizedDirectDamageSpellPattern(
            string normalized)
        {
            return normalized.StartsWith(
                       "shock of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "spear of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "draught of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "lure of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   IsRecognizedAreaDamageSpellPattern(normalized);
        }

        private static bool IsRecognizedDamageOverTimeSpellPattern(
            string normalized)
        {
            return normalized.StartsWith(
                       "tuyen's chant of ",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTeleportationSpell(
            string spellName)
        {
            string normalized =
                NormalizeSpellNameForClassification(spellName);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            // These names intentionally take priority over broad teleport
            // families such as "Circle of ...".
            if (AreaDamageSpellNames.Contains(normalized))
            {
                return false;
            }

            if (TeleportationSpellNames.Contains(normalized))
            {
                return true;
            }

            return normalized.EndsWith(
                       " gate",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith(
                       " portal",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "translocate ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "translocate:",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "evacuate",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "succor",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "ring of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "circle of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "teleport ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "teleport:",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "depart ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "depart:",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "zephyr of ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "alter plane ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "alter plane:",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "plane shift ",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "plane shift:",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith(
                       " relocation",
                       StringComparison.OrdinalIgnoreCase);
        }

        private string BuildDamageSpellCastCorrelationKey(
            string caster,
            string spellName)
        {
            return string.Join(
                "|",
                "damage-cast",
                NormalizeVisualEntity(caster, null),
                NormalizeSpellNameForClassification(spellName));
        }

        private void LearnAreaDamageSpell(
            DateTime timestamp,
            string source,
            string target,
            string rawSpellName,
            DamageSpellCastType damageSpellType)
        {
            string normalizedSpell =
                NormalizeSpellNameForClassification(rawSpellName);

            if (string.IsNullOrWhiteSpace(normalizedSpell) ||
                IsTeleportationSpell(normalizedSpell))
            {
                return;
            }

            string normalizedSource =
                NormalizeVisualEntity(source, null);
            string normalizedTarget =
                NormalizeVisualEntity(target, null);

            if (string.IsNullOrWhiteSpace(normalizedSource) ||
                string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return;
            }

            DateTime cutoff =
                timestamp.AddSeconds(
                    -AreaDamageLearningWindowSeconds);

            foreach (string expiredKey in
                     _recentAreaDamageObservations
                         .Where(entry =>
                             entry.Value.LastTimestamp < cutoff)
                         .Select(entry => entry.Key)
                         .ToList())
            {
                _recentAreaDamageObservations.Remove(expiredKey);
            }

            string observationKey =
                string.Join(
                    "|",
                    normalizedSource,
                    normalizedSpell);

            if (!_recentAreaDamageObservations.TryGetValue(
                    observationKey,
                    out RecentAreaDamageObservation? observation) ||
                timestamp - observation.LastTimestamp >
                    TimeSpan.FromSeconds(
                        AreaDamageLearningWindowSeconds) ||
                timestamp < observation.LastTimestamp)
            {
                observation =
                    new RecentAreaDamageObservation(timestamp);

                _recentAreaDamageObservations[observationKey] =
                    observation;
            }

            observation.LastTimestamp = timestamp;
            observation.Targets.Add(normalizedTarget);

            if (observation.Targets.Count < 2)
            {
                return;
            }

            _learnedAreaDamageSpellNames.Add(normalizedSpell);

            PromoteActiveDamageSpellIndicatorToArea(
                normalizedSource,
                rawSpellName,
                damageSpellType);
        }

        private void PromoteActiveDamageSpellIndicatorToArea(
            string caster,
            string spellName,
            DamageSpellCastType damageSpellType)
        {
            string correlationKey =
                BuildDamageSpellCastCorrelationKey(
                    caster,
                    spellName);

            string glyph =
                GetDamageSpellCastGlyph(
                    damageSpellType,
                    isAreaEffect: true);

            string toolTip =
                damageSpellType ==
                    DamageSpellCastType.DamageOverTime
                    ? $"Casting area effect damage over time: {spellName}"
                    : $"Casting area effect direct damage: {spellName}";

            for (int index = 0;
                 index < _specialIndicatorEvents.Count;
                 index++)
            {
                SpecialIndicatorEvent indicator =
                    _specialIndicatorEvents[index];

                if (indicator.ExpiresAt <= DateTime.Now ||
                    !string.Equals(
                        indicator.CorrelationKey,
                        correlationKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _specialIndicatorEvents[index] =
                    indicator with
                    {
                        Glyph = glyph,
                        ToolTip = toolTip
                    };
            }
        }

        private void LearnDamageSpell(
            string rawSpellName,
            DamageSpellCastType damageSpellType)
        {
            string normalized =
                NormalizeSpellNameForClassification(rawSpellName);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (damageSpellType ==
                DamageSpellCastType.DamageOverTime)
            {
                _learnedDamageOverTimeSpellNames.Add(normalized);
                _learnedDirectDamageSpellNames.Remove(normalized);
                return;
            }

            if (!_learnedDamageOverTimeSpellNames.Contains(normalized))
            {
                _learnedDirectDamageSpellNames.Add(normalized);
            }
        }

        private bool IsHealingSpellName(string spellName)
        {
            if (IsLayOnHandsSpell(spellName))
            {
                return true;
            }

            string normalized =
                NormalizeSpellNameForClassification(spellName);

            if (HealingSpellNames.Contains(normalized) ||
                _learnedHealingSpellNames.Contains(normalized))
            {
                return true;
            }

            string padded = $" {normalized} ";

            return HealingSpellFragments.Any(fragment =>
                padded.Contains(
                    fragment,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsLayOnHandsSpell(
            string spellName)
        {
            string normalized =
                NormalizeSpellNameForClassification(spellName);

            return normalized.Contains(
                       "lay on hands",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(
                       "lay of hands",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryClassifyCrowdControlSpell(
            string spellName,
            out CrowdControlType type,
            out bool isAreaEffect)
        {
            string normalized =
                NormalizeSpellNameForClassification(spellName);

            if (CharmSpellNames.Contains(normalized) ||
                normalized.Contains(
                    "charm",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "beguile",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "allure",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "dictate",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "dominat",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "coerce",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "cajol",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "agacerie",
                    StringComparison.OrdinalIgnoreCase))
            {
                type = CrowdControlType.Charm;
                isAreaEffect = false;
                return true;
            }

            if (RootSpellNames.Contains(normalized) ||
                normalized.Equals(
                    "root",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    " roots",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "immobil",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "fetter",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "paralyz",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "vinelash",
                    StringComparison.OrdinalIgnoreCase))
            {
                type = CrowdControlType.Root;
                isAreaEffect = false;
                return true;
            }

            if (MesmerizeSpellNames.Contains(normalized) ||
                normalized.Contains(
                    "mesmer",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "enthrall",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "pixie strike",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "lucid lullaby",
                    StringComparison.OrdinalIgnoreCase))
            {
                type = CrowdControlType.Mesmerize;
                isAreaEffect =
                    AreaMesmerizeSpellNames.Contains(normalized) ||
                    normalized.Contains(
                        "mesmerization",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(
                        "fascination",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(
                        "entrancing lights",
                        StringComparison.OrdinalIgnoreCase);

                return true;
            }

            if (StunSpellNames.Contains(normalized) ||
                normalized.StartsWith(
                    "color ",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "stun",
                    StringComparison.OrdinalIgnoreCase))
            {
                type = CrowdControlType.Stun;
                isAreaEffect =
                    AreaStunSpellNames.Contains(normalized) ||
                    normalized.StartsWith(
                        "color ",
                        StringComparison.OrdinalIgnoreCase);

                return true;
            }

            if (LullSpellNames.Contains(normalized) ||
                normalized.Contains(
                    "pacify",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(
                    "pacification",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "lull",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "calm",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(
                    "soothe",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(
                    "harmony",
                    StringComparison.OrdinalIgnoreCase))
            {
                type = CrowdControlType.Lull;
                isAreaEffect =
                    normalized.Contains(
                        "harmony",
                        StringComparison.OrdinalIgnoreCase);

                return true;
            }

            type = default;
            isAreaEffect = false;
            return false;
        }

        private static string GetCrowdControlGlyph(
            CrowdControlType type,
            bool isAreaEffect)
        {
            bool useSquare =
                isAreaEffect &&
                (type == CrowdControlType.Mesmerize ||
                 type == CrowdControlType.Stun);

            return useSquare ? "■" : "▲";
        }

        private static global::System.Windows.Media.Brush GetCrowdControlBrush(
            CrowdControlType type)
        {
            return type switch
            {
                CrowdControlType.Charm => CharmIndicatorBrush,
                CrowdControlType.Root => RootIndicatorBrush,
                CrowdControlType.Lull => LullIndicatorBrush,
                CrowdControlType.Mesmerize => MesmerizeIndicatorBrush,
                CrowdControlType.Stun => StunIndicatorBrush,
                _ => FriendlyTextBrush
            };
        }

        private static string GetCrowdControlLabel(
            CrowdControlType type)
        {
            return type switch
            {
                CrowdControlType.Charm => "Charm",
                CrowdControlType.Root => "Root",
                CrowdControlType.Lull => "Lull/Pacify",
                CrowdControlType.Mesmerize => "Mesmerize",
                CrowdControlType.Stun => "Stun",
                _ => "Crowd control"
            };
        }

        private bool IsProtectedFriendlyEntity(
            string entity)
        {
            return !string.IsNullOrWhiteSpace(entity) &&
                   (IsSelf(entity) ||
                    IsOwnedPet(entity) ||
                    IsGroupMember(entity));
        }

        private void MarkHostileIfThreatensProtectedEntity(
            string? source,
            string target)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            source = NormalizeVisualEntity(
                source,
                null);
            target = NormalizeVisualEntity(
                target,
                null);

            RegisterOnlyKnownCombatInteraction(
                source,
                target);

            if (!IsProtectedFriendlyEntity(target) ||
                IsProtectedFriendlyEntity(source))
            {
                return;
            }

            _knownBadGuys.Add(source);
        }

        private bool IsHostileEntity(string entity)
        {
            if (_knownBadGuys.Contains(entity))
            {
                return true;
            }

            if (!_latestTargets.TryGetValue(
                    entity,
                    out TargetEvent? targetEvent))
            {
                return false;
            }

            string target = targetEvent.Target;

            return IsSelf(target) ||
                   IsOwnedPet(target) ||
                   IsGroupMember(target);
        }

        private string NormalizeVisualEntity(
            string raw,
            string? sourceForReflexive)
        {
            string entity = raw
                .Trim()
                .TrimEnd('.', '!')
                .Replace('’', '\'');

            if (entity.Equals(
                    "you",
                    StringComparison.OrdinalIgnoreCase) ||
                entity.Equals(
                    "yourself",
                    StringComparison.OrdinalIgnoreCase) ||
                entity.Equals(
                    "your",
                    StringComparison.OrdinalIgnoreCase))
            {
                return _characterName;
            }

            if (!string.IsNullOrWhiteSpace(sourceForReflexive) &&
                (entity.Equals(
                     "itself",
                     StringComparison.OrdinalIgnoreCase) ||
                 entity.Equals(
                     "himself",
                     StringComparison.OrdinalIgnoreCase) ||
                 entity.Equals(
                     "herself",
                     StringComparison.OrdinalIgnoreCase) ||
                 entity.Equals(
                     "themselves",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return sourceForReflexive;
            }

            return NormalizeEntity(entity);
        }

        private static string CleanSpellName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return raw
                .Trim()
                .Trim('<', '>', ' ', '.', '!')
                .Replace('’', '\'');
        }

        private static string NormalizeSpellNameForClassification(
            string raw)
        {
            string spell = CleanSpellName(raw);

            spell = Regex.Replace(
                spell,
                @"\s+\+\d+$",
                string.Empty,
                RegexOptions.IgnoreCase);

            spell = Regex.Replace(
                spell,
                @"\s+Rk\.\s*[IVX]+$",
                string.Empty,
                RegexOptions.IgnoreCase);

            spell = Regex.Replace(
                spell,
                @"\s+[IVX]{1,5}$",
                string.Empty,
                RegexOptions.IgnoreCase);

            return spell.Trim();
        }

        private static string BuildLayOnHandsCorrelationKey(
            string? healer,
            string target)
        {
            return
                $"lay-on-hands|" +
                $"{healer?.Trim() ?? "unknown"}|" +
                $"{target.Trim()}";
        }

        private static bool TryParseCurrency(
            string message,
            out long copperValue)
        {
            Match match = CorpseCurrencyRegex.Match(message);
            if (!match.Success)
            {
                match = SoldLootCurrencyRegex.Match(message);
            }

            if (!match.Success)
            {
                copperValue = 0;
                return false;
            }

            copperValue = 0;

            foreach (Match coinMatch in
                     CoinAmountRegex.Matches(match.Groups["money"].Value))
            {
                if (!long.TryParse(
                        coinMatch.Groups["amount"].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long amount))
                {
                    continue;
                }

                long multiplier =
                    coinMatch.Groups["coin"].Value.ToLowerInvariant() switch
                    {
                        "platinum" => 1000,
                        "gold" => 100,
                        "silver" => 10,
                        "copper" => 1,
                        _ => 0
                    };

                copperValue += amount * multiplier;
            }

            return copperValue > 0;
        }

        private bool TryParseDamage(
            string message,
            DateTime timestamp,
            out DamageEvent? damageEvent)
        {
            Match match = PossessiveSpellRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    match.Groups["ability"].Value,
                    DamageSpellCastType.DirectDamage);
                return damageEvent != null;
            }

            match = DirectDamageRegex.Match(message);
            if (match.Success)
            {
                DamageSpellCastType? damageSpellType =
                    match.Groups["ability"].Success
                        ? DamageSpellCastType.DirectDamage
                        : null;

                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    match.Groups["ability"].Value,
                    damageSpellType);
                return damageEvent != null;
            }

            match = DotBySourceRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    match.Groups["ability"].Value,
                    DamageSpellCastType.DamageOverTime);
                return damageEvent != null;
            }

            match = YourDotRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    _characterName,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    match.Groups["ability"].Value,
                    DamageSpellCastType.DamageOverTime);
                return damageEvent != null;
            }

            match = YourDamageShieldRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    _characterName,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    string.Empty,
                    null);
                return damageEvent != null;
            }

            match = PossessiveDamageShieldRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value,
                    string.Empty,
                    null);
                return damageEvent != null;
            }

            damageEvent = null;
            return false;
        }

        private DamageEvent? CreateDamageEvent(
            DateTime timestamp,
            string source,
            string target,
            string amountText,
            string ability,
            DamageSpellCastType? damageSpellType)
        {
            if (!int.TryParse(
                    amountText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int amount))
            {
                return null;
            }

            source = NormalizeEntity(source);
            target = NormalizeEntity(target);

            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            ability = CleanSpellName(ability);

            if (damageSpellType.HasValue &&
                !string.IsNullOrWhiteSpace(ability))
            {
                LearnDamageSpell(
                    ability,
                    damageSpellType.Value);

                LearnAreaDamageSpell(
                    timestamp,
                    source,
                    target,
                    ability,
                    damageSpellType.Value);
            }

            return new DamageEvent(
                timestamp,
                source,
                target,
                amount);
        }

        private bool TryParseKill(
            string message,
            out string killedEntity,
            out string killer)
        {
            Match match = YouSlainRegex.Match(message);
            if (match.Success)
            {
                killedEntity = match.Groups["target"].Value;
                killer = _characterName;
                return true;
            }

            match = SlainByRegex.Match(message);
            if (match.Success)
            {
                killedEntity = match.Groups["target"].Value;
                killer = match.Groups["source"].Value;
                return true;
            }

            killedEntity = string.Empty;
            killer = string.Empty;
            return false;
        }

        private void PruneOldDamageEvents()
        {
            if (_fightDamageEvents.Count == 0)
            {
                return;
            }

            DateTime displayEnd = GetFightDisplayEndTime();
            DateTime cutoff = displayEnd.AddSeconds(-DpsWindowSeconds);

            _fightDamageEvents.RemoveAll(
                damage => damage.Timestamp < cutoff);
        }

        private void RefreshDisplay()
        {
            PruneSpecialVisualEvents();
            UpdateTitle();

            DateTime displayEnd = GetFightDisplayEndTime();
            DateTime windowStart = GetDpsWindowStart(displayEnd);
            double durationSeconds = Math.Max(
                1.0,
                (displayEnd - windowStart).TotalSeconds);

            List<DamageEvent> windowEvents = _fightDamageEvents
                .Where(damage =>
                    damage.Timestamp >= windowStart &&
                    damage.Timestamp <= displayEnd)
                .Where(ShouldIncludeDamageEventInCurrentDisplay)
                .ToList();

            DateTime victimCutoff = displayEnd.AddSeconds(-RecentVictimSeconds);

            Dictionary<string, int> recentAttackerCounts = windowEvents
                .Where(damage => damage.Timestamp >= victimCutoff)
                .GroupBy(
                    damage => damage.Target,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(damage => damage.Source)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<DamageEvent>> eventsBySource =
                windowEvents
                    .GroupBy(
                        damage => damage.Source,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToList(),
                        StringComparer.OrdinalIgnoreCase);

            HashSet<string> displaySources =
                eventsBySource.Keys
                    .Where(ShouldShowEntity)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (_alwaysShowGroupMembers)
            {
                // The local character is also part of the group display.
                // Keeping this row present makes it possible to select
                // yourself as main assist before you begin attacking.
                displaySources.Add(_characterName);

                foreach (string member in
                         _groupMembers.Concat(_manualGroupMembers))
                {
                    displaySources.Add(member);
                }
            }

            foreach (string entity in
                     _specialIndicatorEvents
                         .Select(entry => entry.Entity)
                         .Concat(_healingAnimationUntil.Keys))
            {
                displaySources.Add(entity);
            }

            if (_showSpellCastingSubtext)
            {
                foreach (string entity in _latestSpellCasts.Keys)
                {
                    displaySources.Add(entity);
                }
            }

            // Apply the selected data filter after every possible row source
            // has been added. This prevents temporary cast, healing, or CC
            // visuals from bypassing Remove unknowns or Only knowns.
            displaySources.RemoveWhere(
                entity => !ShouldShowEntity(entity));

            string? mainAssistTarget = null;
            if (_fightActive &&
                _showMainAssistIndicators &&
                !string.IsNullOrWhiteSpace(_mainAssistName) &&
                _latestTargets.TryGetValue(
                    _mainAssistName,
                    out TargetEvent? assistTargetEvent))
            {
                mainAssistTarget = assistTargetEvent.Target;
            }

            List<DamageRow> newRows = displaySources
                .Select(source =>
                {
                    List<DamageEvent> sourceEvents =
                        eventsBySource.TryGetValue(
                            source,
                            out List<DamageEvent>? existingEvents)
                            ? existingEvents
                            : new List<DamageEvent>();

                    long totalDamage =
                        sourceEvents.Sum(
                            damage => (long)damage.Amount);
                    double dps = totalDamage / durationSeconds;

                    int markerCount = recentAttackerCounts.TryGetValue(
                        source,
                        out int count)
                        ? count
                        : 0;

                    bool isGroupMember = IsGroupMember(source);
                    bool isMainAssist =
                        _showMainAssistIndicators &&
                        !string.IsNullOrWhiteSpace(_mainAssistName) &&
                        source.Equals(
                            _mainAssistName,
                            StringComparison.OrdinalIgnoreCase);

                    string? currentTarget =
                        _latestTargets.TryGetValue(
                            source,
                            out TargetEvent? targetEvent)
                            ? targetEvent.Target
                            : null;

                    bool canShowMismatch =
                        _fightActive &&
                        _showMainAssistIndicators &&
                        !isMainAssist &&
                        (IsSelf(source) || isGroupMember) &&
                        !string.IsNullOrWhiteSpace(mainAssistTarget) &&
                        !string.IsNullOrWhiteSpace(currentTarget);

                    // This intentionally includes every other group member
                    // when the local character is the selected main assist.
                    // Their latest target is compared against your target.

                    bool hasAssistMismatch =
                        canShowMismatch &&
                        !currentTarget!.Equals(
                            mainAssistTarget,
                            StringComparison.OrdinalIgnoreCase);

                    string displayName =
                        BuildDisplayName(source, markerCount);
                    (global::System.Windows.Media.Brush rowBrush, global::System.Windows.Media.Brush textBrush) =
                        GetRowColors(source);

                    bool hasMainAssistTarget =
                        isMainAssist &&
                        _fightActive &&
                        !string.IsNullOrWhiteSpace(currentTarget);

                    List<RowIndicator> indicators =
                        _specialIndicatorEvents
                            .Where(entry =>
                                entry.Entity.Equals(
                                    source,
                                    StringComparison.OrdinalIgnoreCase))
                            .OrderBy(entry => entry.CreatedAt)
                            .Select(entry => new RowIndicator
                            {
                                Glyph = entry.Glyph,
                                Foreground = entry.Foreground,
                                ToolTip = entry.ToolTip,
                                IsFlashing = entry.IsFlashing,
                                Opacity = CalculateIndicatorOpacity(
                                    entry)
                            })
                            .ToList();

                    bool showHealingAnimation =
                        _healingAnimationUntil.TryGetValue(
                            source,
                            out DateTime healingUntil) &&
                        healingUntil > DateTime.Now;

                    RecentSpellCast? latestSpellCast = null;
                    bool hasCastingSubtext =
                        _showSpellCastingSubtext &&
                        _latestSpellCasts.TryGetValue(
                            source,
                            out latestSpellCast) &&
                        latestSpellCast.ExpiresAt > DateTime.Now;

                    return new DamageRow
                    {
                        RawEntityName = source,
                        DisplayName = displayName,
                        DamageText = totalDamage.ToString(
                            "N0",
                            CultureInfo.CurrentCulture),
                        DpsText = dps.ToString(
                            "N1",
                            CultureInfo.CurrentCulture),
                        RawDamage = totalDamage,
                        RowBrush = rowBrush,
                        TextBrush = textBrush,
                        NumericTextAlignment =
                            _numbersRightAligned
                                ? TextAlignment.Right
                                : TextAlignment.Left,
                        IsGroupMember = isGroupMember,
                        IsMainAssist = isMainAssist,
                        HasAssistMismatch = hasAssistMismatch,
                        HasMainAssistTarget = hasMainAssistTarget,
                        MainAssistTargetSubtext = hasMainAssistTarget
                            ? $"targeting {currentTarget}"
                            : string.Empty,
                        TargetSubtext = hasAssistMismatch
                            ? $"targeting {currentTarget}"
                            : string.Empty,
                        HasCastingSubtext = hasCastingSubtext,
                        CastingSpellName = hasCastingSubtext
                            ? latestSpellCast!.Spell
                            : string.Empty,
                        CastingSpellBrush = hasCastingSubtext
                            ? latestSpellCast!.Foreground
                            : UnclassifiedSpellIndicatorBrush,
                        Indicators = indicators,
                        ShowHealingAnimation = showHealingAnimation
                    };
                })
                .OrderByDescending(row => row.RawDamage)
                .ThenBy(
                    row => row.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            _rows.Clear();
            foreach (DamageRow row in newRows)
            {
                _rows.Add(row);
            }

            if (string.IsNullOrWhiteSpace(_activeFilePath))
            {
                return;
            }

            string statusPrefix =
                IsDebugMode
                    ? "DEBUG snapshot — "
                    : string.Empty;

            if (_fightActive)
            {
                SetStatus(
                    $"{statusPrefix}Combat active — {Path.GetFileName(_activeFilePath)}");
            }
            else if (_fightStart.HasValue)
            {
                SetStatus(
                    $"{statusPrefix}Last fight complete — {Path.GetFileName(_activeFilePath)}");
            }
            else
            {
                SetStatus(
                    $"{statusPrefix}Waiting for damage — {Path.GetFileName(_activeFilePath)}");
            }
        }

        private DateTime GetFightDisplayEndTime()
        {
            if (_fightEnd.HasValue)
            {
                return _fightEnd.Value;
            }

            if (_fightActive)
            {
                if (_latestLogTimestamp.HasValue &&
                    Math.Abs((DateTime.Now - _latestLogTimestamp.Value).TotalMinutes) <= 10)
                {
                    return DateTime.Now;
                }

                if (_lastCombatActivity.HasValue)
                {
                    return _lastCombatActivity.Value;
                }
            }

            return _latestLogTimestamp ?? DateTime.Now;
        }

        private DateTime GetDpsWindowStart(DateTime displayEnd)
        {
            DateTime rollingStart = displayEnd.AddSeconds(-DpsWindowSeconds);

            if (_fightStart.HasValue && _fightStart.Value > rollingStart)
            {
                return _fightStart.Value;
            }

            return rollingStart;
        }

        private string BuildDisplayName(string source, int markerCount)
        {
            string name = source;

            if (IsOwnedPet(source))
            {
                name = $"{source} [{_characterName}'s pet]";
            }

            if (markerCount > 0)
            {
                name += " " + new string('!', markerCount);
            }

            return name;
        }

        private bool ShouldShowEntity(string source)
        {
            return _dataFilterMode switch
            {
                DataFilterMode.AllData =>
                    true,

                DataFilterMode.RemoveUnknowns =>
                    IsSelf(source) ||
                    IsOwnedPet(source) ||
                    IsGroupMember(source) ||
                    _knownBadGuys.Contains(source),

                DataFilterMode.OnlyKnowns =>
                    IsEntityInOnlyKnownCombatScope(
                        source),

                _ =>
                    true
            };
        }

        private (global::System.Windows.Media.Brush RowBrush, global::System.Windows.Media.Brush TextBrush) GetRowColors(string source)
        {
            if (IsSelf(source))
            {
                return (SelfRowBrush, SelfTextBrush);
            }

            if (IsOwnedPet(source))
            {
                return (PetRowBrush, PetTextBrush);
            }

            if (IsGroupMember(source))
            {
                return (GroupRowBrush, GroupTextBrush);
            }

            if (_knownBadGuys.Contains(source))
            {
                return (EnemyRowBrush, EnemyTextBrush);
            }

            return (FriendlyRowBrush, FriendlyTextBrush);
        }

        private void UpdateTitle()
        {
            if (string.IsNullOrWhiteSpace(_activeFilePath))
            {
                Title = "Spyxy's DPS Meter";
                TitleText.Text = Title;
                return;
            }

            List<string> identityParts = new();

            if (_showPlayerName)
            {
                identityParts.Add(_characterName);
            }

            if (_showServerName)
            {
                identityParts.Add(_serverName);
            }

            List<string> titleParts = new();

            if (IsDebugMode)
            {
                titleParts.Add("DEBUG SNAPSHOT");
            }

            titleParts.Add(
                identityParts.Count > 0
                    ? string.Join(" - ", identityParts)
                    : "DPS Meter");

            if (_showExperiencePerHour)
            {
                double sessionRate = CalculateSessionExperiencePerHour();
                titleParts.Add($"XP/h {sessionRate:0.00}%");
            }

            if (_showLastTenExperience)
            {
                double rollingRate = CalculateRollingExperiencePerHour();
                titleParts.Add($"Last 10 XP/h {rollingRate:0.00}%");
            }

            if (_showPlatinumPerHour)
            {
                double platinumRate = CalculatePlatinumPerHour();
                titleParts.Add($"{platinumRate:0.00}p/h");
            }

            Title = string.Join(" | ", titleParts);
            TitleText.Text = Title;
        }

        private double CalculateSessionExperiencePerHour()
        {
            if (!_sessionStart.HasValue || _experienceEvents.Count == 0)
            {
                return 0;
            }

            DateTime end = GetSessionReferenceTime();
            double hours = Math.Max(
                1.0 / 3600.0,
                (end - _sessionStart.Value).TotalHours);

            return _experienceEvents.Sum(entry => entry.Percent) / hours;
        }

        private double CalculateRollingExperiencePerHour()
        {
            if (_experienceEvents.Count == 0)
            {
                return 0;
            }

            List<ExperienceEvent> recent = _experienceEvents
                .TakeLast(10)
                .ToList();

            if (recent.Count < 2)
            {
                return CalculateSessionExperiencePerHour();
            }

            double averageExperience = recent.Average(entry => entry.Percent);
            double intervalSeconds =
                (recent[^1].Timestamp - recent[0].Timestamp).TotalSeconds /
                (recent.Count - 1);

            if (intervalSeconds <= 0)
            {
                return CalculateSessionExperiencePerHour();
            }

            return averageExperience * (3600.0 / intervalSeconds);
        }

        private double CalculatePlatinumPerHour()
        {
            if (_currencyEvents.Count == 0)
            {
                return 0;
            }

            DateTime end = GetSessionReferenceTime();
            DateTime oneHourCutoff =
                end.AddMinutes(-PlatinumHistoryMinutes);

            List<CurrencyEvent> retainedEvents = _currencyEvents
                .Where(entry =>
                    entry.Timestamp >= oneHourCutoff &&
                    entry.Timestamp <= end)
                .OrderBy(entry => entry.Timestamp)
                .ToList();

            if (retainedEvents.Count == 0)
            {
                return 0;
            }

            return _useThrottledPlatinumRate
                ? CalculateThrottledPlatinumPerHour(
                    end,
                    retainedEvents)
                : CalculateNormalPlatinumPerHour(
                    end,
                    retainedEvents);
        }

        private static double CalculateNormalPlatinumPerHour(
            DateTime end,
            List<CurrencyEvent> retainedEvents)
        {
            DateTime firstCurrencyTime =
                retainedEvents[0].Timestamp;
            double elapsedHours = Math.Max(
                1.0 / 3600.0,
                (end - firstCurrencyTime).TotalHours);

            long copper = retainedEvents.Sum(
                entry => entry.CopperValue);

            return (copper / 1000.0) / elapsedHours;
        }

        private static double CalculateThrottledPlatinumPerHour(
            DateTime end,
            List<CurrencyEvent> retainedEvents)
        {
            DateTime rateWindowCutoff =
                end.AddMinutes(-PlatinumRateWindowMinutes);
            DateTime firstTrackedCurrency =
                retainedEvents[0].Timestamp;

            DateTime effectiveWindowStart =
                firstTrackedCurrency > rateWindowCutoff
                    ? firstTrackedCurrency
                    : rateWindowCutoff;

            List<CurrencyEvent> recentEvents = retainedEvents
                .Where(entry =>
                    entry.Timestamp >= effectiveWindowStart)
                .ToList();

            if (recentEvents.Count == 0)
            {
                return 0;
            }

            long recentCopper = recentEvents.Sum(
                entry => entry.CopperValue);

            double elapsedMinutes = Math.Max(
                PlatinumRateWindowMinutes,
                (end - effectiveWindowStart).TotalMinutes);

            return (recentCopper / 1000.0) /
                   (elapsedMinutes / 60.0);
        }

        private void PruneCurrencyEvents(DateTime referenceTime)
        {
            DateTime cutoff =
                referenceTime.AddMinutes(-PlatinumHistoryMinutes);

            _currencyEvents.RemoveAll(
                entry => entry.Timestamp < cutoff);
        }

        private DateTime GetSessionReferenceTime()
        {
            if (_latestLogTimestamp.HasValue &&
                Math.Abs((DateTime.Now - _latestLogTimestamp.Value).TotalMinutes) <= 10)
            {
                return DateTime.Now;
            }

            return _latestLogTimestamp ?? DateTime.Now;
        }

        private void ResetAllState()
        {
            _pendingText = string.Empty;
            _fileOffset = 0;
            _lastWriteTimeUtc = DateTime.MinValue;
            _latestLogTimestamp = null;
            _sessionStart = null;

            _fightDamageEvents.Clear();
            _experienceEvents.Clear();
            _currencyEvents.Clear();
            _recentLogLines.Clear();
            _petOwners.Clear();
            _knownBadGuys.Clear();
            _onlyKnownCombatEntities.Clear();
            _groupMembers.Clear();
            _latestTargets.Clear();
            ClearSpecialVisualEvents();

            _pendingGroupInviter = null;
            _pendingPartyExperienceTimestamp = null;

            _fightActive = false;
            _fightStart = null;
            _fightEnd = null;
            _lastCombatActivity = null;
            _pendingBarrierTimestamp = null;
            _pendingBarrierWallClock = null;

            _rows.Clear();
        }

        private void ResetForNewLogin(DateTime timestamp)
        {
            _sessionStart = timestamp;
            _experienceEvents.Clear();
            _currencyEvents.Clear();
            _recentLogLines.Clear();
            _recentLogLines.Enqueue(new CachedLogLine(timestamp, $"[{timestamp:ddd MMM dd HH:mm:ss yyyy}] Welcome to EverQuest Legends!"));
            _petOwners.Clear();
            _knownBadGuys.Clear();
            _onlyKnownCombatEntities.Clear();
            _groupMembers.Clear();
            _latestTargets.Clear();
            ClearSpecialVisualEvents();

            _pendingGroupInviter = null;
            _pendingPartyExperienceTimestamp = null;

            _fightDamageEvents.Clear();
            _fightActive = false;
            _fightStart = null;
            _fightEnd = null;
            _lastCombatActivity = null;
            _pendingBarrierTimestamp = null;
            _pendingBarrierWallClock = null;
        }

        private string NormalizeEntity(string raw)
        {
            string entity = raw.Trim().TrimEnd('.', '!');

            if (entity.Equals("you", StringComparison.OrdinalIgnoreCase) ||
                entity.Equals("your", StringComparison.OrdinalIgnoreCase))
            {
                return _characterName;
            }

            if (entity.Equals("your pet", StringComparison.OrdinalIgnoreCase) ||
                entity.Equals("my pet", StringComparison.OrdinalIgnoreCase))
            {
                const string genericPetName = "Your pet";
                _petOwners[genericPetName] = _characterName;
                return genericPetName;
            }

            return entity;
        }

        private bool IsSelf(string entity)
        {
            return entity.Equals(
                _characterName,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOwnedPet(string entity)
        {
            return _petOwners.ContainsKey(entity);
        }

        private bool IsGroupMember(string entity)
        {
            return _groupMembers.Contains(entity) ||
                   _manualGroupMembers.Contains(entity);
        }

        private bool IsUnknownEntity(string entity)
        {
            return !IsSelf(entity) &&
                   !IsOwnedPet(entity) &&
                   !IsGroupMember(entity) &&
                   !_knownBadGuys.Contains(entity);
        }

        private static bool TryGetMessage(
            string rawLine,
            out DateTime timestamp,
            out string message)
        {
            Match match = LogLineRegex.Match(rawLine);
            if (!match.Success)
            {
                timestamp = default;
                message = string.Empty;
                return false;
            }

            if (!DateTime.TryParseExact(
                    match.Groups["timestamp"].Value,
                    TimestampFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out timestamp))
            {
                message = string.Empty;
                return false;
            }

            message = match.Groups["message"].Value.Trim();
            return true;
        }

        private static TailReadResult ReadLastLinesShared(
            string path,
            int maximumLines)
        {
            using FileStream stream = OpenSharedRead(path);
            long length = stream.Length;

            if (length == 0)
            {
                return new TailReadResult(new List<string>(), 0);
            }

            const int blockSize = 16 * 1024;
            byte[] buffer = new byte[blockSize];

            long scanPosition = length;
            long startPosition = 0;
            int newlineCount = 0;
            bool foundStart = false;

            while (scanPosition > 0 && !foundStart)
            {
                int bytesToRead = (int)Math.Min(blockSize, scanPosition);
                scanPosition -= bytesToRead;

                stream.Seek(scanPosition, SeekOrigin.Begin);
                int bytesRead = stream.Read(buffer, 0, bytesToRead);

                for (int i = bytesRead - 1; i >= 0; i--)
                {
                    if (buffer[i] != (byte)'\n')
                    {
                        continue;
                    }

                    newlineCount++;
                    if (newlineCount > maximumLines)
                    {
                        startPosition = scanPosition + i + 1;
                        foundStart = true;
                        break;
                    }
                }
            }

            stream.Seek(startPosition, SeekOrigin.Begin);

            long remaining = length - startPosition;
            using MemoryStream capturedBytes = new();
            byte[] readBuffer = new byte[16 * 1024];

            while (remaining > 0)
            {
                int requested = (int)Math.Min(readBuffer.Length, remaining);
                int read = stream.Read(readBuffer, 0, requested);
                if (read <= 0)
                {
                    break;
                }

                capturedBytes.Write(readBuffer, 0, read);
                remaining -= read;
            }

            string text = Encoding.UTF8
                .GetString(capturedBytes.ToArray())
                .TrimStart('\uFEFF')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            List<string> lines = text
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(maximumLines)
                .ToList();

            return new TailReadResult(lines, length);
        }

        private static FileStream OpenSharedRead(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);
        }

        private static string CapitalizeFirst(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return char.ToUpperInvariant(value[0]) + value[1..];
        }

        private static global::System.Windows.Media.Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush =
                new(global::System.Windows.Media.Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                DragMove();
            }
            finally
            {
                QueueWindowPlacementSave();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            global::System.Windows.Controls.ContextMenu? menu = SettingsButton.ContextMenu;
            if (menu == null)
            {
                return;
            }

            PopulateGroupMemberMenus();
            UpdateLogDirectoryMenuItems();

            menu.PlacementTarget = SettingsButton;
            menu.IsOpen = true;
            e.Handled = true;
        }


        private void FeatureGuideMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            global::System.Windows.Controls.TextBlock featureText = new()
            {
                Text = FeatureHelpText,
                TextWrapping =
                    global::System.Windows.TextWrapping.Wrap,
                Foreground =
                    new global::System.Windows.Media.SolidColorBrush(
                        global::System.Windows.Media.Color.FromRgb(
                            232,
                            236,
                            241)),
                FontSize = 12,
                LineHeight = 18,
                Padding =
                    new global::System.Windows.Thickness(6)
            };

            global::System.Windows.Controls.ScrollViewer scrollViewer = new()
            {
                Content = featureText,
                VerticalScrollBarVisibility =
                    global::System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    global::System.Windows.Controls.ScrollBarVisibility.Disabled,
                Margin =
                    new global::System.Windows.Thickness(
                        14,
                        14,
                        14,
                        8)
            };

            global::System.Windows.Controls.Button projectButton = new()
            {
                Content = "GitHub Project",
                Width = 110,
                Height = 30,
                Margin =
                    new global::System.Windows.Thickness(
                        0,
                        0,
                        8,
                        0)
            };

            global::System.Windows.Controls.Button closeButton = new()
            {
                Content = "Close",
                Width = 90,
                Height = 30,
                IsDefault = true,
                IsCancel = true
            };

            global::System.Windows.Controls.StackPanel buttons = new()
            {
                Orientation =
                    global::System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment =
                    global::System.Windows.HorizontalAlignment.Right,
                Margin =
                    new global::System.Windows.Thickness(
                        0,
                        0,
                        14,
                        14)
            };
            buttons.Children.Add(projectButton);
            buttons.Children.Add(closeButton);

            global::System.Windows.Controls.Grid layout = new();
            layout.RowDefinitions.Add(
                new global::System.Windows.Controls.RowDefinition
                {
                    Height =
                        new global::System.Windows.GridLength(
                            1,
                            global::System.Windows.GridUnitType.Star)
                });
            layout.RowDefinitions.Add(
                new global::System.Windows.Controls.RowDefinition
                {
                    Height =
                        global::System.Windows.GridLength.Auto
                });

            global::System.Windows.Controls.Grid.SetRow(
                scrollViewer,
                0);
            global::System.Windows.Controls.Grid.SetRow(
                buttons,
                1);

            layout.Children.Add(scrollViewer);
            layout.Children.Add(buttons);

            global::System.Windows.Controls.Border frame = new()
            {
                Background =
                    new global::System.Windows.Media.SolidColorBrush(
                        global::System.Windows.Media.Color.FromRgb(
                            27,
                            31,
                            37)),
                BorderBrush =
                    new global::System.Windows.Media.SolidColorBrush(
                        global::System.Windows.Media.Color.FromRgb(
                            87,
                            96,
                            106)),
                BorderThickness =
                    new global::System.Windows.Thickness(1),
                Child = layout
            };

            global::System.Windows.Window dialog = new()
            {
                Owner = this,
                Title =
                    "Spyxy's DPS Meter — Feature Guide",
                Width = 700,
                Height = 740,
                MinWidth = 460,
                MinHeight = 340,
                MaxHeight =
                    Math.Max(
                        380,
                        global::System.Windows.SystemParameters
                            .WorkArea.Height * 0.9),
                WindowStartupLocation =
                    global::System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode =
                    global::System.Windows.ResizeMode.CanResize,
                ShowInTaskbar = false,
                Topmost = true,
                Background =
                    new global::System.Windows.Media.SolidColorBrush(
                        global::System.Windows.Media.Color.FromRgb(
                            27,
                            31,
                            37)),
                Content = frame
            };

            projectButton.Click +=
                (_, _) => OpenProjectPage(ProjectUrl);
            closeButton.Click +=
                (_, _) => dialog.Close();

            dialog.ShowDialog();
        }

        private void OpenProjectPageMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenProjectPage(ProjectUrl);
        }

        private void OpenProjectPage(
            string? address)
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            string.IsNullOrWhiteSpace(address)
                                ? ProjectUrl
                                : address,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Unable to open the project page: {ex.Message}");
            }
        }


        private void ChangeLogDirectoryMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            string initialDirectory =
                Directory.Exists(_logDirectory)
                    ? _logDirectory
                    : Directory.Exists(DefaultLogDirectory)
                        ? DefaultLogDirectory
                        : string.Empty;

            using global::System.Windows.Forms.FolderBrowserDialog dialog =
                new()
                {
                    Description =
                        "Choose the folder containing EverQuest Legends " +
                        "eqlog_CHARACTER_SERVER.txt files.",
                    ShowNewFolderButton = false,
                    UseDescriptionForTitle = true,
                    SelectedPath = initialDirectory
                };

            global::System.Windows.Forms.DialogResult result =
                dialog.ShowDialog();

            if (result !=
                    global::System.Windows.Forms.DialogResult.OK ||
                string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            ChangeLogDirectory(
                dialog.SelectedPath,
                isRevertToDefault: false);
        }

        private void RevertLogDirectoryMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ChangeLogDirectory(
                DefaultLogDirectory,
                isRevertToDefault: true);
        }

        private void ChangeLogDirectory(
            string directory,
            bool isRevertToDefault)
        {
            string normalizedDirectory;

            try
            {
                normalizedDirectory =
                    NormalizeDirectoryPath(directory);
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Unable to use the selected log folder: {ex.Message}");
                return;
            }

            if (!isRevertToDefault &&
                !Directory.Exists(normalizedDirectory))
            {
                SetStatus(
                    $"The selected log folder does not exist: " +
                    $"{normalizedDirectory}");
                return;
            }

            if (DirectoryPathsEqual(
                    _logDirectory,
                    normalizedDirectory))
            {
                UpdateLogDirectoryMenuItems();

                SetStatus(
                    isRevertToDefault
                        ? "The default log directory is already selected."
                        : "That log directory is already selected.");

                return;
            }

            _logDirectory = normalizedDirectory;
            SaveSettings();
            UpdateLogDirectoryMenuItems();

            RestartLogMonitoringForDirectoryChange();

            if (string.IsNullOrWhiteSpace(_activeFilePath))
            {
                SetStatus(
                    Directory.Exists(_logDirectory)
                        ? $"No eqlog_CHARACTER_SERVER.txt file was found in {_logDirectory}."
                        : $"Log folder not found: {_logDirectory}");
            }
        }

        private void RestartLogMonitoringForDirectoryChange()
        {
            _activeFilePath = null;
            _characterName = "Character";
            _serverName = "Server";

            ResetAllState();
            RefreshDisplay();
            DetectLatestLogFile();
        }

        private void UpdateLogDirectoryMenuItems()
        {
            string displayName =
                GetDirectoryDisplayName(_logDirectory);

            CurrentLogDirectoryMenuItem.Header =
                $"Current: {displayName}";
            CurrentLogDirectoryMenuItem.ToolTip =
                _logDirectory;
            LogDirectoryMenuItem.ToolTip =
                _logDirectory;

            RevertLogDirectoryMenuItem.IsEnabled =
                !DirectoryPathsEqual(
                    _logDirectory,
                    DefaultLogDirectory);
        }

        private static string GetDirectoryDisplayName(
            string directory)
        {
            try
            {
                DirectoryInfo info =
                    new(directory);

                return string.IsNullOrWhiteSpace(info.Name)
                    ? info.FullName
                    : info.Name;
            }
            catch
            {
                return directory;
            }
        }

        private static string NormalizeDirectoryPath(
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return DefaultLogDirectory;
            }

            return new DirectoryInfo(
                    directory.Trim())
                .FullName;
        }

        private static bool DirectoryPathsEqual(
            string left,
            string right)
        {
            try
            {
                return string.Equals(
                    NormalizeDirectoryPath(left),
                    NormalizeDirectoryPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(
                    left.Trim(),
                    right.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private void DisplaySettingMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string settingName)
            {
                return;
            }

            bool isEnabled = menuItem.IsChecked;

            switch (settingName)
            {
                case "PlayerName":
                    _showPlayerName = isEnabled;
                    break;

                case "ServerName":
                    _showServerName = isEnabled;
                    break;

                case "ExperiencePerHour":
                    _showExperiencePerHour = isEnabled;
                    break;

                case "LastTenExperience":
                    _showLastTenExperience = isEnabled;
                    break;

                case "PlatinumPerHour":
                    _showPlatinumPerHour = isEnabled;
                    break;

                case "AlwaysShowGroupMembers":
                    _alwaysShowGroupMembers = isEnabled;
                    break;

                case "MainAssistIndicators":
                    _showMainAssistIndicators = isEnabled;
                    break;

                case "SpellCastingSubtext":
                    _showSpellCastingSubtext = isEnabled;
                    if (!isEnabled)
                    {
                        _latestSpellCasts.Clear();
                    }
                    break;
            }

            SaveSettings();
            RefreshDisplay();
        }

        private void NumericAlignmentMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string alignment)
            {
                return;
            }

            _numbersRightAligned =
                alignment.Equals(
                    "Right",
                    StringComparison.OrdinalIgnoreCase);

            ApplySettingsToMenuItems();
            SaveSettings();
            RefreshDisplay();
        }

        private void PlatinumModeMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string mode)
            {
                return;
            }

            _useThrottledPlatinumRate =
                mode.Equals(
                    "Throttled",
                    StringComparison.OrdinalIgnoreCase);

            ApplySettingsToMenuItems();
            SaveSettings();
            RefreshDisplay();
        }

        private void RefreshRateMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string intervalText ||
                !double.TryParse(
                    intervalText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double requestedInterval))
            {
                return;
            }

            _logRefreshIntervalSeconds =
                NormalizeLogRefreshIntervalSeconds(
                    requestedInterval);

            ApplyReadTimerInterval(
                IsVisible
                    ? _logRefreshIntervalSeconds
                    : BackgroundLogRefreshIntervalSeconds);

            ApplySettingsToMenuItems();
            SaveSettings();
        }

        private void DpsGrid_MouseRightButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            DependencyObject? current =
                e.OriginalSource as DependencyObject;

            while (current != null &&
                   current is not DataGridRow)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is not DataGridRow row ||
                row.Item is not DamageRow damageRow)
            {
                return;
            }

            global::System.Windows.Controls.MenuItem setMainAssistItem = new()
            {
                Header = "Set as Main Assist",
                CommandParameter = damageRow,
                IsEnabled =
                    damageRow.IsGroupMember ||
                    IsSelf(damageRow.RawEntityName)
            };
            setMainAssistItem.Click +=
                SetMainAssistMenuItem_Click;

            global::System.Windows.Controls.MenuItem clearMainAssistItem = new()
            {
                Header = "Clear Main Assist",
                IsEnabled =
                    !string.IsNullOrWhiteSpace(_mainAssistName)
            };
            clearMainAssistItem.Click +=
                ClearMainAssistMenuItem_Click;

            global::System.Windows.Controls.ContextMenu menu = new();
            menu.Items.Add(setMainAssistItem);
            menu.Items.Add(clearMainAssistItem);
            menu.PlacementTarget = row;
            menu.IsOpen = true;

            e.Handled = true;
        }

        private void SetMainAssistMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.CommandParameter is not DamageRow row ||
                (!row.IsGroupMember &&
                 !IsSelf(row.RawEntityName)))
            {
                SetStatus(
                    "Only yourself or a detected/manually tagged " +
                    "group member can be selected as main assist.");
                return;
            }

            _mainAssistName = row.RawEntityName;
            SaveSettings();
            RefreshDisplay();
            SetStatus($"{_mainAssistName} is now the main assist.");
        }

        private void ClearMainAssistMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            _mainAssistName = null;
            SaveSettings();
            RefreshDisplay();
            SetStatus("Main assist cleared.");
        }

        private void PopulateGroupMemberMenus()
        {
            TagGroupMemberMenuItem.Items.Clear();
            RemoveGroupMemberMenuItem.Items.Clear();

            List<string> unknownEntities = _fightDamageEvents
                .Select(entry => entry.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(IsUnknownEntity)
                .OrderBy(entity => entity, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unknownEntities.Count == 0)
            {
                TagGroupMemberMenuItem.Items.Add(
                    new global::System.Windows.Controls.MenuItem
                    {
                        Header = "No unclassified entities in the current meter",
                        IsEnabled = false
                    });
            }
            else
            {
                foreach (string entity in unknownEntities)
                {
                    global::System.Windows.Controls.MenuItem item = new()
                    {
                        Header = entity,
                        Tag = entity
                    };

                    item.Click += ManualGroupMemberAdd_Click;
                    TagGroupMemberMenuItem.Items.Add(item);
                }
            }

            List<string> manualMembers = _manualGroupMembers
                .OrderBy(entity => entity, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (manualMembers.Count == 0)
            {
                RemoveGroupMemberMenuItem.Items.Add(
                    new global::System.Windows.Controls.MenuItem
                    {
                        Header = "No manually tagged group members",
                        IsEnabled = false
                    });
            }
            else
            {
                foreach (string entity in manualMembers)
                {
                    global::System.Windows.Controls.MenuItem item = new()
                    {
                        Header = entity,
                        Tag = entity
                    };

                    item.Click += ManualGroupMemberRemove_Click;
                    RemoveGroupMemberMenuItem.Items.Add(item);
                }
            }
        }

        private void ManualGroupMemberAdd_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string entity ||
                string.IsNullOrWhiteSpace(entity))
            {
                return;
            }

            _manualGroupMembers.Add(entity);

            if (_dataFilterMode ==
                DataFilterMode.OnlyKnowns)
            {
                RebuildOnlyKnownCombatScope();
            }

            SaveSettings();
            PopulateGroupMemberMenus();
            RefreshDisplay();
            SetStatus($"{entity} tagged as a group member.");
        }

        private void ManualGroupMemberRemove_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string entity ||
                string.IsNullOrWhiteSpace(entity))
            {
                return;
            }

            _manualGroupMembers.Remove(entity);

            if (_dataFilterMode ==
                DataFilterMode.OnlyKnowns)
            {
                RebuildOnlyKnownCombatScope();
            }

            if (!IsGroupMember(entity) &&
                entity.Equals(
                    _mainAssistName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _mainAssistName = null;
            }

            SaveSettings();
            PopulateGroupMemberMenus();
            RefreshDisplay();
            SetStatus($"{entity} removed from manual group members.");
        }

        private void DataFilteringMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not global::System.Windows.Controls.MenuItem menuItem ||
                menuItem.Tag is not string requestedMode)
            {
                return;
            }

            _dataFilterMode =
                ParseDataFilterMode(
                    requestedMode,
                    legacyShowUnknownEntities: null);

            if (_dataFilterMode ==
                DataFilterMode.OnlyKnowns)
            {
                RebuildOnlyKnownCombatScope();
            }

            ApplySettingsToMenuItems();
            SaveSettings();
            RefreshDisplay();

            SetStatus(
                $"Data filtering: {GetDataFilterDisplayName(_dataFilterMode)}.");
        }

        private static DataFilterMode ParseDataFilterMode(
            string? value,
            bool? legacyShowUnknownEntities)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse(
                    value,
                    ignoreCase: true,
                    out DataFilterMode parsedMode))
            {
                return parsedMode switch
                {
                    DataFilterMode.AllData =>
                        DataFilterMode.AllData,
                    DataFilterMode.RemoveUnknowns =>
                        DataFilterMode.RemoveUnknowns,
                    DataFilterMode.OnlyKnowns =>
                        DataFilterMode.OnlyKnowns,
                    _ =>
                        DataFilterMode.AllData
                };
            }

            return legacyShowUnknownEntities == false
                ? DataFilterMode.RemoveUnknowns
                : DataFilterMode.AllData;
        }

        private static string GetDataFilterDisplayName(
            DataFilterMode mode)
        {
            return mode switch
            {
                DataFilterMode.AllData =>
                    "All data",
                DataFilterMode.RemoveUnknowns =>
                    "Remove unknowns",
                DataFilterMode.OnlyKnowns =>
                    "Only knowns",
                _ =>
                    "All data"
            };
        }

        private bool IsDirectProtectedCombatInteraction(
            string? source,
            string? target)
        {
            return (!string.IsNullOrWhiteSpace(source) &&
                    IsProtectedFriendlyEntity(source)) ||
                   (!string.IsNullOrWhiteSpace(target) &&
                    IsProtectedFriendlyEntity(target));
        }

        private bool IsEntityInOnlyKnownCombatScope(
            string entity)
        {
            return !string.IsNullOrWhiteSpace(entity) &&
                   (IsProtectedFriendlyEntity(entity) ||
                    _onlyKnownCombatEntities.Contains(entity));
        }

        private void RegisterOnlyKnownCombatInteraction(
            string? source,
            string? target)
        {
            if (_dataFilterMode !=
                    DataFilterMode.OnlyKnowns ||
                string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            source =
                NormalizeVisualEntity(
                    source,
                    null);
            target =
                NormalizeVisualEntity(
                    target,
                    null);

            bool sourceIsProtected =
                IsProtectedFriendlyEntity(source);
            bool targetIsProtected =
                IsProtectedFriendlyEntity(target);

            if (sourceIsProtected &&
                !targetIsProtected)
            {
                _onlyKnownCombatEntities.Add(target);
                _knownBadGuys.Add(target);
            }

            if (targetIsProtected &&
                !sourceIsProtected)
            {
                _onlyKnownCombatEntities.Add(source);
                _knownBadGuys.Add(source);
            }
        }

        private void RebuildOnlyKnownCombatScope()
        {
            _onlyKnownCombatEntities.Clear();

            if (_dataFilterMode !=
                DataFilterMode.OnlyKnowns)
            {
                return;
            }

            foreach (DamageEvent damageEvent in
                     _fightDamageEvents
                         .OrderBy(
                             entry => entry.Timestamp))
            {
                RegisterOnlyKnownCombatInteraction(
                    damageEvent.Source,
                    damageEvent.Target);
            }

            foreach (KeyValuePair<string, TargetEvent> targetPair in
                     _latestTargets)
            {
                RegisterOnlyKnownCombatInteraction(
                    targetPair.Key,
                    targetPair.Value.Target);
            }
        }

        private bool ShouldAcceptDamageEventForCurrentFilter(
            DamageEvent damageEvent)
        {
            if (_dataFilterMode !=
                DataFilterMode.OnlyKnowns)
            {
                return true;
            }

            if (IsDirectProtectedCombatInteraction(
                    damageEvent.Source,
                    damageEvent.Target))
            {
                return true;
            }

            return _fightActive &&
                   IsEntityInOnlyKnownCombatScope(
                       damageEvent.Source) &&
                   IsEntityInOnlyKnownCombatScope(
                       damageEvent.Target);
        }

        private bool ShouldIncludeDamageEventInCurrentDisplay(
            DamageEvent damageEvent)
        {
            if (_dataFilterMode !=
                DataFilterMode.OnlyKnowns)
            {
                return true;
            }

            return IsEntityInOnlyKnownCombatScope(
                       damageEvent.Source) &&
                   IsEntityInOnlyKnownCombatScope(
                       damageEvent.Target);
        }

        private bool ShouldProcessKillForCurrentFilter(
            string killedEntity,
            string killer)
        {
            if (_dataFilterMode !=
                DataFilterMode.OnlyKnowns)
            {
                return true;
            }

            RegisterOnlyKnownCombatInteraction(
                killer,
                killedEntity);

            return IsProtectedFriendlyEntity(killedEntity) ||
                   IsProtectedFriendlyEntity(killer) ||
                   _onlyKnownCombatEntities.Contains(
                       killedEntity);
        }

        private int GetCurrentMaximumLinesPerRead()
        {
            double intervalSeconds =
                _readTimer.Interval.TotalSeconds;

            if (intervalSeconds <= 0.2001)
            {
                return 200;
            }

            if (intervalSeconds <= 0.5001)
            {
                return 500;
            }

            return 1000;
        }

        private static double NormalizeLogRefreshIntervalSeconds(
            double intervalSeconds)
        {
            double[] supportedIntervals =
            {
                0.2,
                0.5,
                1.0,
                2.0,
                3.0,
                5.0
            };

            foreach (double supportedInterval in supportedIntervals)
            {
                if (Math.Abs(
                        intervalSeconds -
                        supportedInterval) <
                    0.0001)
                {
                    return supportedInterval;
                }
            }

            return DefaultLogRefreshIntervalSeconds;
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(SettingsFilePath);
                MeterSettings? settings =
                    JsonSerializer.Deserialize<MeterSettings>(json);

                if (settings == null)
                {
                    return;
                }

                _showPlayerName = settings.ShowPlayerName;
                _showServerName = settings.ShowServerName;
                _showExperiencePerHour =
                    settings.ShowExperiencePerHour;
                _showLastTenExperience =
                    settings.ShowLastTenExperience;
                _showPlatinumPerHour =
                    settings.ShowPlatinumPerHour;
                _dataFilterMode =
                    ParseDataFilterMode(
                        settings.DataFilteringMode,
                        settings.ShowUnknownEntities);
                _numbersRightAligned =
                    settings.RightAlignNumbers;
                _useThrottledPlatinumRate =
                    settings.UseThrottledPlatinumRate;
                _alwaysShowGroupMembers =
                    settings.AlwaysShowGroupMembers;
                _showMainAssistIndicators =
                    settings.ShowMainAssistIndicators;
                _showSpellCastingSubtext =
                    settings.ShowSpellCastingSubtext;
                _instantMessengerEnabled =
                    settings.InstantMessengerEnabled;

                _instantMessengerChannelSettings.Clear();

                foreach (KeyValuePair<
                             string,
                             InstantMessengerChannelPreference>
                         pair in
                         settings.InstantMessengerChannelSettings ??
                         new Dictionary<
                             string,
                             InstantMessengerChannelPreference>())
                {
                    string key =
                        NormalizeInstantMessengerChannelName(
                            pair.Key);

                    _instantMessengerChannelSettings[key] =
                        pair.Value ??
                        CreateDefaultInstantMessengerChannelPreference(
                            key);
                }

                _logRefreshIntervalSeconds =
                    NormalizeLogRefreshIntervalSeconds(
                        settings.LogRefreshIntervalSeconds);

                try
                {
                    _logDirectory =
                        string.IsNullOrWhiteSpace(
                            settings.LogDirectory)
                            ? DefaultLogDirectory
                            : NormalizeDirectoryPath(
                                settings.LogDirectory);
                }
                catch
                {
                    _logDirectory =
                        DefaultLogDirectory;
                }

                _mainAssistName =
                    string.IsNullOrWhiteSpace(
                        settings.MainAssistName)
                        ? null
                        : settings.MainAssistName.Trim();

                _manualGroupMembers.Clear();

                foreach (string member in
                         settings.ManualGroupMembers ??
                         new List<string>())
                {
                    string trimmed = member.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        _manualGroupMembers.Add(trimmed);
                    }
                }

                ApplySavedWindowPlacement(settings);
            }
            catch (Exception ex)
            {
                SetStatus($"Unable to read settings.json: {ex.Message}");
            }
        }

        private void ApplySettingsToMenuItems()
        {
            PlayerNameMenuItem.IsChecked = _showPlayerName;
            ServerNameMenuItem.IsChecked = _showServerName;
            ExperiencePerHourMenuItem.IsChecked =
                _showExperiencePerHour;
            LastTenExperienceMenuItem.IsChecked =
                _showLastTenExperience;
            PlatinumPerHourMenuItem.IsChecked =
                _showPlatinumPerHour;
            DataFilterAllDataMenuItem.IsChecked =
                _dataFilterMode ==
                DataFilterMode.AllData;
            DataFilterRemoveUnknownsMenuItem.IsChecked =
                _dataFilterMode ==
                DataFilterMode.RemoveUnknowns;
            DataFilterOnlyKnownsMenuItem.IsChecked =
                _dataFilterMode ==
                DataFilterMode.OnlyKnowns;
            AlwaysShowGroupMembersMenuItem.IsChecked =
                _alwaysShowGroupMembers;
            MainAssistIndicatorsMenuItem.IsChecked =
                _showMainAssistIndicators;
            SpellCastingSubtextMenuItem.IsChecked =
                _showSpellCastingSubtext;
            InstantMessengerMenuItem.IsChecked =
                _instantMessengerEnabled;
            UpdateInstantMessengerButtonState();

            RefreshRate02MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 0.2) <
                0.0001;
            RefreshRate05MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 0.5) <
                0.0001;
            RefreshRate10MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 1.0) <
                0.0001;
            RefreshRate20MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 2.0) <
                0.0001;
            RefreshRate30MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 3.0) <
                0.0001;
            RefreshRate50MenuItem.IsChecked =
                Math.Abs(
                    _logRefreshIntervalSeconds - 5.0) <
                0.0001;

            NumberAlignmentLeftMenuItem.IsChecked =
                !_numbersRightAligned;
            NumberAlignmentRightMenuItem.IsChecked =
                _numbersRightAligned;

            PlatinumModeNormalMenuItem.IsChecked =
                !_useThrottledPlatinumRate;
            PlatinumModeThrottledMenuItem.IsChecked =
                _useThrottledPlatinumRate;

            UpdateLogDirectoryMenuItems();
        }

        private void SaveSettings()
        {
            try
            {
                Rect windowBounds =
                    GetWindowPlacementBounds();

                MeterSettings settings = new()
                {
                    ShowPlayerName = _showPlayerName,
                    ShowServerName = _showServerName,
                    ShowExperiencePerHour =
                        _showExperiencePerHour,
                    ShowLastTenExperience =
                        _showLastTenExperience,
                    ShowPlatinumPerHour =
                        _showPlatinumPerHour,
                    DataFilteringMode =
                        _dataFilterMode.ToString(),
                    ShowUnknownEntities =
                        _dataFilterMode ==
                        DataFilterMode.AllData,
                    RightAlignNumbers =
                        _numbersRightAligned,
                    UseThrottledPlatinumRate =
                        _useThrottledPlatinumRate,
                    AlwaysShowGroupMembers =
                        _alwaysShowGroupMembers,
                    ShowMainAssistIndicators =
                        _showMainAssistIndicators,
                    ShowSpellCastingSubtext =
                        _showSpellCastingSubtext,
                    InstantMessengerEnabled =
                        _instantMessengerEnabled,
                    InstantMessengerChannelSettings =
                        _instantMessengerChannelSettings
                            .ToDictionary(
                                pair => pair.Key,
                                pair => pair.Value,
                                StringComparer.OrdinalIgnoreCase),
                    LogRefreshIntervalSeconds =
                        _logRefreshIntervalSeconds,
                    LogDirectory =
                        _logDirectory,
                    WindowLeft =
                        windowBounds.IsEmpty
                            ? null
                            : windowBounds.Left,
                    WindowTop =
                        windowBounds.IsEmpty
                            ? null
                            : windowBounds.Top,
                    WindowWidth =
                        windowBounds.IsEmpty
                            ? null
                            : windowBounds.Width,
                    WindowHeight =
                        windowBounds.IsEmpty
                            ? null
                            : windowBounds.Height,
                    MainAssistName =
                        _mainAssistName,
                    ManualGroupMembers = _manualGroupMembers
                        .OrderBy(
                            entity => entity,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                string json = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                SetStatus($"Unable to save settings.json: {ex.Message}");
            }
        }


        private void ProjectLink_RequestNavigate(
            object sender,
            global::System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;
            OpenProjectPage(
                e.Uri?.AbsoluteUri ??
                ProjectUrl);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime resetTime = GetSessionReferenceTime();

            _sessionStart = resetTime;
            _experienceEvents.Clear();
            _currencyEvents.Clear();

            _fightDamageEvents.Clear();
            _fightActive = false;
            _fightStart = null;
            _fightEnd = null;
            _lastCombatActivity = null;
            _pendingBarrierTimestamp = null;
            _pendingBarrierWallClock = null;
            _pendingPartyExperienceTimestamp = null;
            _latestTargets.Clear();
            _onlyKnownCombatEntities.Clear();
            ClearSpecialVisualEvents();

            _rows.Clear();
            RefreshDisplay();
            SetStatus("DPS, XP and platinum tracking reset.");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideToSystemTray();
        }

        public sealed class DamageRow
        {
            public string RawEntityName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string DamageText { get; set; } = string.Empty;
            public string DpsText { get; set; } = string.Empty;
            public string TargetSubtext { get; set; } = string.Empty;
            public string MainAssistTargetSubtext { get; set; } = string.Empty;
            public string CastingSpellName { get; set; } = string.Empty;
            public global::System.Windows.Media.Brush CastingSpellBrush { get; set; } =
                UnclassifiedSpellIndicatorBrush;
            public long RawDamage { get; set; }
            public global::System.Windows.Media.Brush RowBrush { get; set; } = FriendlyRowBrush;
            public global::System.Windows.Media.Brush TextBrush { get; set; } = FriendlyTextBrush;
            public TextAlignment NumericTextAlignment { get; set; } =
                TextAlignment.Left;
            public bool IsGroupMember { get; set; }
            public bool IsMainAssist { get; set; }
            public bool HasAssistMismatch { get; set; }
            public bool HasMainAssistTarget { get; set; }
            public bool HasCastingSubtext { get; set; }
            public bool ShowHealingAnimation { get; set; }
            public List<RowIndicator> Indicators { get; set; } = new();
        }

        public sealed class RowIndicator
        {
            public string Glyph { get; set; } = string.Empty;
            public global::System.Windows.Media.Brush Foreground { get; set; } =
                FriendlyTextBrush;
            public string ToolTip { get; set; } = string.Empty;
            public bool IsFlashing { get; set; }
            public double Opacity { get; set; } = 1;
        }

        public sealed class MeterSettings
        {
            public bool ShowPlayerName { get; set; } = true;
            public bool ShowServerName { get; set; } = true;
            public bool ShowExperiencePerHour { get; set; } = true;
            public bool ShowLastTenExperience { get; set; } = true;
            public bool ShowPlatinumPerHour { get; set; } = true;
            public string? DataFilteringMode { get; set; }
            public bool? ShowUnknownEntities { get; set; }
            public bool RightAlignNumbers { get; set; }
            public bool UseThrottledPlatinumRate { get; set; } = true;
            public bool AlwaysShowGroupMembers { get; set; } = true;
            public bool ShowMainAssistIndicators { get; set; } = true;
            public bool ShowSpellCastingSubtext { get; set; } = true;
            public bool InstantMessengerEnabled { get; set; } = true;
            public Dictionary<
                string,
                InstantMessengerChannelPreference>
                InstantMessengerChannelSettings { get; set; } =
                    new();
            public double LogRefreshIntervalSeconds { get; set; } =
                DefaultLogRefreshIntervalSeconds;
            public string LogDirectory { get; set; } =
                DefaultLogDirectory;
            public double? WindowLeft { get; set; }
            public double? WindowTop { get; set; }
            public double? WindowWidth { get; set; }
            public double? WindowHeight { get; set; }
            public string? MainAssistName { get; set; }
            public List<string> ManualGroupMembers { get; set; } = new();
        }

        private enum DataFilterMode
        {
            AllData,
            RemoveUnknowns,
            OnlyKnowns
        }

        private enum DamageSpellCastType
        {
            DirectDamage,
            DamageOverTime
        }

        private enum CrowdControlType
        {
            Charm,
            Root,
            Lull,
            Mesmerize,
            Stun
        }

        private sealed record ParsedSpellCast(
            string Caster,
            string Spell);

        private sealed record ParsedHealEvent(
            string? Healer,
            string Target,
            string Spell);

        private sealed record RecentCrowdControlCast(
            CrowdControlType Type,
            bool IsAreaEffect,
            DateTime ExpiresAt);

        private sealed record RecentLayOnHandsCast(
            string Caster,
            DateTime ExpiresAt);

        private sealed record RecentSpellCast(
            string Spell,
            global::System.Windows.Media.Brush Foreground,
            DateTime ExpiresAt);

        private sealed record RecentSpellCastSource(
            string Caster,
            string Spell,
            DateTime ExpiresAt);

        private sealed class RecentAreaDamageObservation
        {
            public RecentAreaDamageObservation(
                DateTime timestamp)
            {
                LastTimestamp = timestamp;
            }

            public DateTime LastTimestamp { get; set; }

            public HashSet<string> Targets { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed record SpecialIndicatorEvent(
            string Entity,
            string Glyph,
            global::System.Windows.Media.Brush Foreground,
            string ToolTip,
            DateTime ExpiresAt,
            DateTime LogTimestamp,
            string? CorrelationKey)
        {
            public DateTime CreatedAt { get; } = DateTime.Now;
            public bool IsFlashing { get; } = false;
        }

        private sealed record DamageEvent(
            DateTime Timestamp,
            string Source,
            string Target,
            int Amount);

        private sealed record ExperienceEvent(
            DateTime Timestamp,
            double Percent);

        private sealed record CurrencyEvent(
            DateTime Timestamp,
            long CopperValue);

        private sealed record TargetEvent(
            DateTime Timestamp,
            string Target);

        private sealed record CachedLogLine(
            DateTime Timestamp,
            string RawLine);

        private sealed record TailReadResult(
            List<string> Lines,
            long FileLength);
    }
}
