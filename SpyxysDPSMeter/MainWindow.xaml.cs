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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace SpyxysDPSMeter
{
    public partial class MainWindow : Window
    {
        private const string DefaultLogDirectory =
            @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\Logs";

        private const string ProjectUrl =
            "https://github.com/khadesh/SpyxysDPSMeter";

        private const int MaximumLinesPerRead = 1000;
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

        private readonly ObservableCollection<DamageRow> _rows = new();
        private readonly DispatcherTimer _readTimer;
        private readonly DispatcherTimer _fileScanTimer;

        private global::System.Windows.Forms.NotifyIcon? _trayIcon;
        private global::System.Windows.Forms.ContextMenuStrip? _trayMenu;
        private global::System.Drawing.Icon? _trayIconImage;
        private bool _exitRequested;

        private readonly List<DamageEvent> _fightDamageEvents = new();
        private readonly List<ExperienceEvent> _experienceEvents = new();
        private readonly List<CurrencyEvent> _currencyEvents = new();
        private readonly Queue<CachedLogLine> _recentLogLines = new();
        private readonly Dictionary<string, string> _petOwners =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _knownBadGuys =
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
        private readonly HashSet<string> _learnedHealingSpellNames =
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
        private bool _showUnknownEntities = true;
        private bool _numbersRightAligned;
        private bool _useThrottledPlatinumRate = true;
        private bool _alwaysShowGroupMembers = true;
        private bool _showMainAssistIndicators = true;
        private string? _mainAssistName;

        public MainWindow()
        {
            InitializeComponent();

            DpsGrid.ItemsSource = _rows;

            LoadSettings();
            ApplySettingsToMenuItems();

            _readTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _readTimer.Tick += ReadTimer_Tick;

            _fileScanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _fileScanTimer.Tick += FileScanTimer_Tick;

            InitializeTrayIcon();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
            SaveSettings();
            DisposeTrayIcon();
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
            ShowInTaskbar = false;
            Hide();
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

            Activate();
            Focus();
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

                ResetAllState();

                TailReadResult tail = ReadLastLinesShared(path, MaximumLinesPerRead);
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
                    // This should never happen in a one-second interval. Recover by
                    // loading only the newest 1000 lines instead of allocating gigabytes.
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

                IEnumerable<string> completeLines = pieces
                    .Take(Math.Max(0, completeCount))
                    .Where(line => !string.IsNullOrWhiteSpace(line));

                List<string> lines = completeLines.TakeLast(MaximumLinesPerRead).ToList();

                foreach (string line in lines)
                {
                    ProcessRawLine(line, isInitialLoad: false);
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

        private void ProcessRawLine(string rawLine, bool isInitialLoad)
        {
            if (!TryGetMessage(rawLine, out DateTime timestamp, out string message))
            {
                return;
            }

            if (_latestLogTimestamp == null || timestamp > _latestLogTimestamp)
            {
                _latestLogTimestamp = timestamp;
            }

            _recentLogLines.Enqueue(new CachedLogLine(timestamp, rawLine));
            while (_recentLogLines.Count > MaximumLinesPerRead)
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
                string pet = NormalizeEntity(petMatch.Groups["pet"].Value);
                if (!string.IsNullOrWhiteSpace(pet))
                {
                    _petOwners[pet] = _characterName;
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

                if (IsSelf(killer) || IsOwnedPet(killer))
                {
                    _knownBadGuys.Add(killedEntity);
                }

                if (_fightActive)
                {
                    _pendingBarrierTimestamp = timestamp;
                    _pendingBarrierWallClock = isInitialLoad ? null : DateTime.Now;
                }

                return;
            }

            if (TryParseDamage(message, timestamp, out DamageEvent? damageEvent))
            {
                HandleDamageEvent(damageEvent);
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
                StartNewFight(damageEvent.Timestamp);
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

                if (TryClassifyCrowdControlSpell(
                        spellName,
                        out CrowdControlType type,
                        out bool isAreaEffect))
                {
                    if (!isInitialLoad)
                    {
                        string target = NormalizeVisualEntity(
                            receivedSpellMatch.Groups["target"].Value,
                            null);

                        AddCrowdControlLandedIndicator(
                            target,
                            type,
                            isAreaEffect,
                            logTimestamp);
                    }

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
            DateTime logTimestamp)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            AddSpecialIndicator(
                target,
                GetCrowdControlGlyph(type, isAreaEffect),
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

            List<string> expiredAnimations =
                _healingAnimationUntil
                    .Where(pair => pair.Value <= now)
                    .Select(pair => pair.Key)
                    .ToList();

            foreach (string entity in expiredAnimations)
            {
                _healingAnimationUntil.Remove(entity);
            }
        }

        private void ClearSpecialVisualEvents()
        {
            _specialIndicatorEvents.Clear();
            _recentCrowdControlCasts.Clear();
            _recentLayOnHandsCasts.Clear();
            _healingAnimationUntil.Clear();
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
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            match = DirectDamageRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            match = DotBySourceRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            match = YourDotRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    _characterName,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            match = YourDamageShieldRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    _characterName,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            match = PossessiveDamageShieldRegex.Match(message);
            if (match.Success)
            {
                damageEvent = CreateDamageEvent(
                    timestamp,
                    match.Groups["source"].Value,
                    match.Groups["target"].Value,
                    match.Groups["amount"].Value);
                return damageEvent != null;
            }

            damageEvent = null;
            return false;
        }

        private DamageEvent? CreateDamageEvent(
            DateTime timestamp,
            string source,
            string target,
            string amountText)
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

            return new DamageEvent(timestamp, source, target, amount);
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

            if (_fightActive)
            {
                SetStatus($"Combat active — {Path.GetFileName(_activeFilePath)}");
            }
            else if (_fightStart.HasValue)
            {
                SetStatus($"Last fight complete — {Path.GetFileName(_activeFilePath)}");
            }
            else
            {
                SetStatus($"Waiting for damage — {Path.GetFileName(_activeFilePath)}");
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
            if (_showUnknownEntities)
            {
                return true;
            }

            return IsSelf(source) ||
                   IsOwnedPet(source) ||
                   IsGroupMember(source) ||
                   _knownBadGuys.Contains(source);
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

            List<string> titleParts = new()
            {
                identityParts.Count > 0
                    ? string.Join(" - ", identityParts)
                    : "DPS Meter"
            };

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
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
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

                case "UnknownEntities":
                    _showUnknownEntities = isEnabled;
                    break;

                case "AlwaysShowGroupMembers":
                    _alwaysShowGroupMembers = isEnabled;
                    break;

                case "MainAssistIndicators":
                    _showMainAssistIndicators = isEnabled;
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
                        Header = "No unknown entities in the current meter",
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
                _showUnknownEntities =
                    settings.ShowUnknownEntities;
                _numbersRightAligned =
                    settings.RightAlignNumbers;
                _useThrottledPlatinumRate =
                    settings.UseThrottledPlatinumRate;
                _alwaysShowGroupMembers =
                    settings.AlwaysShowGroupMembers;
                _showMainAssistIndicators =
                    settings.ShowMainAssistIndicators;

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
            UnknownEntitiesMenuItem.IsChecked =
                _showUnknownEntities;
            AlwaysShowGroupMembersMenuItem.IsChecked =
                _alwaysShowGroupMembers;
            MainAssistIndicatorsMenuItem.IsChecked =
                _showMainAssistIndicators;

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
                    ShowUnknownEntities =
                        _showUnknownEntities,
                    RightAlignNumbers =
                        _numbersRightAligned,
                    UseThrottledPlatinumRate =
                        _useThrottledPlatinumRate,
                    AlwaysShowGroupMembers =
                        _alwaysShowGroupMembers,
                    ShowMainAssistIndicators =
                        _showMainAssistIndicators,
                    LogDirectory =
                        _logDirectory,
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
            try
            {
                string address =
                    e.Uri?.AbsoluteUri ??
                    ProjectUrl;

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = address,
                        UseShellExecute = true
                    });

                e.Handled = true;
            }
            catch (Exception ex)
            {
                e.Handled = true;
                SetStatus(
                    $"Unable to open the project page: {ex.Message}");
            }
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
            public long RawDamage { get; set; }
            public global::System.Windows.Media.Brush RowBrush { get; set; } = FriendlyRowBrush;
            public global::System.Windows.Media.Brush TextBrush { get; set; } = FriendlyTextBrush;
            public TextAlignment NumericTextAlignment { get; set; } =
                TextAlignment.Left;
            public bool IsGroupMember { get; set; }
            public bool IsMainAssist { get; set; }
            public bool HasAssistMismatch { get; set; }
            public bool HasMainAssistTarget { get; set; }
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
            public bool ShowUnknownEntities { get; set; } = true;
            public bool RightAlignNumbers { get; set; }
            public bool UseThrottledPlatinumRate { get; set; } = true;
            public bool AlwaysShowGroupMembers { get; set; } = true;
            public bool ShowMainAssistIndicators { get; set; } = true;
            public string LogDirectory { get; set; } =
                DefaultLogDirectory;
            public string? MainAssistName { get; set; }
            public List<string> ManualGroupMembers { get; set; } = new();
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
