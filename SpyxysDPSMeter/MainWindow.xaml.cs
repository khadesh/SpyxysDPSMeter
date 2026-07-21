using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private const string LogDirectory =
            @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\Logs";

        private const int MaximumLinesPerRead = 1000;
        private const int DpsWindowSeconds = 30;
        private const int RecentVictimSeconds = 5;
        private const int FightBarrierSeconds = 3;
        private const int PlatinumHistoryMinutes = 60;
        private const int PlatinumRateWindowMinutes = 3;

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

        private static readonly string[] TimestampFormats =
        {
            "ddd MMM d HH:mm:ss yyyy",
            "ddd MMM dd HH:mm:ss yyyy"
        };

        private static readonly Brush SelfRowBrush = FrozenBrush(90, 112, 83, 18);
        private static readonly Brush SelfTextBrush = FrozenBrush(255, 255, 216, 92);

        private static readonly Brush GroupRowBrush = FrozenBrush(90, 94, 74, 28);
        private static readonly Brush GroupTextBrush = FrozenBrush(255, 246, 220, 137);

        private static readonly Brush PetRowBrush = FrozenBrush(90, 35, 102, 50);
        private static readonly Brush PetTextBrush = FrozenBrush(255, 142, 232, 148);

        private static readonly Brush EnemyRowBrush = FrozenBrush(90, 111, 34, 34);
        private static readonly Brush EnemyTextBrush = FrozenBrush(255, 255, 142, 142);

        private static readonly Brush FriendlyRowBrush = FrozenBrush(90, 37, 76, 115);
        private static readonly Brush FriendlyTextBrush = FrozenBrush(255, 161, 207, 255);

        private readonly ObservableCollection<DamageRow> _rows = new();
        private readonly DispatcherTimer _readTimer;
        private readonly DispatcherTimer _fileScanTimer;

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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DetectLatestLogFile();
            _readTimer.Start();
            _fileScanTimer.Start();
            RefreshDisplay();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _readTimer.Stop();
            _fileScanTimer.Stop();
            SaveSettings();
        }

        private void FileScanTimer_Tick(object? sender, EventArgs e)
        {
            DetectLatestLogFile();
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
                if (!Directory.Exists(LogDirectory))
                {
                    SetStatus($"Log folder not found: {LogDirectory}");
                    return;
                }

                FileInfo? newest = Directory
                    .EnumerateFiles(LogDirectory, "eqlog_*_*.txt", SearchOption.TopDirectoryOnly)
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
                    (Brush rowBrush, Brush textBrush) =
                        GetRowColors(source);

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
                        TargetSubtext = hasAssistMismatch
                            ? $"targetting {currentTarget}"
                            : string.Empty
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

        private (Brush RowBrush, Brush TextBrush) GetRowColors(string source)
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

        private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush =
                new(Color.FromArgb(alpha, red, green, blue));
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
            ContextMenu? menu = SettingsButton.ContextMenu;
            if (menu == null)
            {
                return;
            }

            PopulateGroupMemberMenus();

            menu.PlacementTarget = SettingsButton;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void DisplaySettingMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
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
            if (sender is not MenuItem menuItem ||
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
            if (sender is not MenuItem menuItem ||
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

            MenuItem setMainAssistItem = new()
            {
                Header = "Set as Main Assist",
                CommandParameter = damageRow,
                IsEnabled =
                    damageRow.IsGroupMember ||
                    IsSelf(damageRow.RawEntityName)
            };
            setMainAssistItem.Click +=
                SetMainAssistMenuItem_Click;

            MenuItem clearMainAssistItem = new()
            {
                Header = "Clear Main Assist",
                IsEnabled =
                    !string.IsNullOrWhiteSpace(_mainAssistName)
            };
            clearMainAssistItem.Click +=
                ClearMainAssistMenuItem_Click;

            ContextMenu menu = new();
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
            if (sender is not MenuItem menuItem ||
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
                    new MenuItem
                    {
                        Header = "No unknown entities in the current meter",
                        IsEnabled = false
                    });
            }
            else
            {
                foreach (string entity in unknownEntities)
                {
                    MenuItem item = new()
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
                    new MenuItem
                    {
                        Header = "No manually tagged group members",
                        IsEnabled = false
                    });
            }
            else
            {
                foreach (string entity in manualMembers)
                {
                    MenuItem item = new()
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
            if (sender is not MenuItem menuItem ||
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
            if (sender is not MenuItem menuItem ||
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

            _rows.Clear();
            RefreshDisplay();
            SetStatus("DPS, XP and platinum tracking reset.");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public sealed class DamageRow
        {
            public string RawEntityName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string DamageText { get; set; } = string.Empty;
            public string DpsText { get; set; } = string.Empty;
            public string TargetSubtext { get; set; } = string.Empty;
            public long RawDamage { get; set; }
            public Brush RowBrush { get; set; } = FriendlyRowBrush;
            public Brush TextBrush { get; set; } = FriendlyTextBrush;
            public TextAlignment NumericTextAlignment { get; set; } =
                TextAlignment.Left;
            public bool IsGroupMember { get; set; }
            public bool IsMainAssist { get; set; }
            public bool HasAssistMismatch { get; set; }
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
            public string? MainAssistName { get; set; }
            public List<string> ManualGroupMembers { get; set; } = new();
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
