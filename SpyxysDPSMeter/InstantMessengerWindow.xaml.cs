using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpyxysDPSMeter
{
    public sealed class InstantMessengerChannelPreference
    {
        public bool AutoMarkRead { get; set; }
        public bool AutoMarkImportant { get; set; }
        public bool MarkMyNameImportant { get; set; } = true;
        public bool IgnoreAll { get; set; }
    }

    public sealed class InstantMessageRecord
    {
        public DateTime Timestamp { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string? Recipient { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsOutgoing { get; set; }
    }

    public partial class InstantMessengerWindow : Window
    {
        private const string AllChannelName = "All";
        private const string SayChannelName = "Say";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Dictionary<string, InstantMessengerChannelPreference>
            _channelPreferences;
        private readonly Dictionary<string, ChannelView> _channelViews =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ChatMessageState> _messages = new();
        private readonly DispatcherTimer _importantBlinkTimer;

        private string _characterName = "Character";
        private string _serverName = "Server";
        private string? _historyFilePath;
        private bool _shutdownRequested;
        private bool _blinkVisible = true;
        private bool _isApplyingPreferenceControls;
        private bool _hasLoaded;

        public InstantMessengerWindow(
            Dictionary<string, InstantMessengerChannelPreference>
                channelPreferences)
        {
            _channelPreferences =
                channelPreferences ??
                throw new ArgumentNullException(
                    nameof(channelPreferences));

            InitializeComponent();

            _importantBlinkTimer =
                new DispatcherTimer(
                    DispatcherPriority.Background)
                {
                    Interval =
                        TimeSpan.FromMilliseconds(500)
                };
            _importantBlinkTimer.Tick +=
                ImportantBlinkTimer_Tick;
            _importantBlinkTimer.Start();

            EnsureAllView();
        }

        public event Action<int, bool>? UnreadStateChanged;

        public event Action? ChannelSettingsChanged;

        public void SetIdentity(
            string characterName,
            string serverName)
        {
            string normalizedCharacter =
                string.IsNullOrWhiteSpace(characterName)
                    ? "Character"
                    : characterName.Trim();
            string normalizedServer =
                string.IsNullOrWhiteSpace(serverName)
                    ? "Server"
                    : serverName.Trim();

            bool identityChanged =
                !normalizedCharacter.Equals(
                    _characterName,
                    StringComparison.OrdinalIgnoreCase) ||
                !normalizedServer.Equals(
                    _serverName,
                    StringComparison.OrdinalIgnoreCase);

            _characterName = normalizedCharacter;
            _serverName = normalizedServer;

            IdentityText.Text =
                $"{_characterName} - {_serverName}";

            if (!identityChanged &&
                !string.IsNullOrWhiteSpace(
                    _historyFilePath))
            {
                return;
            }

            _historyFilePath =
                BuildHistoryFilePath(
                    _characterName,
                    _serverName);

            LoadPersistedHistory();
        }

        public void ShowMessenger()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState ==
                WindowState.Minimized)
            {
                WindowState =
                    WindowState.Normal;
            }

            Activate();
            Focus();
        }

        public void HideMessenger()
        {
            Hide();
        }

        public void PublishUnreadState()
        {
            RefreshUnreadState();
        }

        public void Shutdown()
        {
            _shutdownRequested = true;
            _importantBlinkTimer.Stop();
            Close();
        }

        public void AddLiveMessage(
            InstantMessageRecord message)
        {
            AddMessageCore(
                message,
                forceImportant: false,
                persist: true,
                suppressSound: false,
                isDebugReplay: false);
        }

        public void ReplaceWithDebugMessages(
            IEnumerable<InstantMessageRecord> messages)
        {
            _messages.Clear();
            ResetChannelViewsForIdentity();

            int loadedCount = 0;

            foreach (InstantMessageRecord message in
                     messages ??
                     Enumerable.Empty<InstantMessageRecord>())
            {
                bool forceImportant =
                    !NormalizeChannelName(
                        message.Channel)
                    .Equals(
                        SayChannelName,
                        StringComparison.OrdinalIgnoreCase);

                AddMessageCore(
                    message,
                    forceImportant,
                    persist: false,
                    suppressSound: true,
                    isDebugReplay: true);

                loadedCount++;
            }

            SetStatus(
                $"Debug chat snapshot loaded {loadedCount:N0} messages. " +
                "All non-say messages are important.");
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton !=
                System.Windows.Input.MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                WindowState =
                    WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The mouse may be released before DragMove begins.
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            HideMessenger();
        }


        private void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            _hasLoaded = true;

            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(
                    () =>
                    {
                        foreach (ChannelView view in
                                 _channelViews.Values)
                        {
                            view.ScrollViewer
                                .ScrollToEnd();
                        }
                    }));
        }

        private void Window_Closing(
            object? sender,
            System.ComponentModel.CancelEventArgs e)
        {
            if (_shutdownRequested)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        }

        private void Window_Activated(
            object sender,
            EventArgs e)
        {
            RefreshUnreadState();
        }

        private void Window_Deactivated(
            object sender,
            EventArgs e)
        {
            RefreshUnreadState();
        }

        private void ChannelTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshUnreadState();
        }

        private void ImportantBlinkTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _blinkVisible =
                !_blinkVisible;

            foreach (ChannelView view in
                     _channelViews.Values)
            {
                if (view.ImportantCount <= 0)
                {
                    view.ImportantText.Opacity = 0;
                    continue;
                }

                view.ImportantText.Opacity =
                    _blinkVisible
                        ? 1
                        : 0.15;
            }
        }

        private void ScrollViewer_ScrollChanged(
            object sender,
            ScrollChangedEventArgs e)
        {
            if (!IsActive ||
                sender is not ScrollViewer scrollViewer ||
                scrollViewer.Tag is not string channelName ||
                e.VerticalChange == 0 ||
                !IsAtBottom(scrollViewer))
            {
                return;
            }

            MarkChannelRead(channelName);
        }

        private void AddMessageCore(
            InstantMessageRecord message,
            bool forceImportant,
            bool persist,
            bool suppressSound,
            bool isDebugReplay)
        {
            if (message == null)
            {
                return;
            }

            string channel =
                NormalizeChannelName(
                    message.Channel);

            message.Channel = channel;
            message.Sender =
                string.IsNullOrWhiteSpace(
                    message.Sender)
                    ? "Unknown"
                    : message.Sender.Trim();
            message.Text =
                message.Text?.Trim() ??
                string.Empty;

            ChannelView channelView =
                EnsureChannelView(channel);
            InstantMessengerChannelPreference preference =
                GetPreference(channel);

            if (preference.IgnoreAll)
            {
                RefreshUnreadState();
                return;
            }

            bool containsCharacterName =
                !string.IsNullOrWhiteSpace(
                    _characterName) &&
                message.Text.IndexOf(
                    _characterName,
                    StringComparison.OrdinalIgnoreCase) >= 0;

            bool shouldBeImportant =
                forceImportant ||
                preference.AutoMarkImportant ||
                (preference.MarkMyNameImportant &&
                 containsCharacterName);

            bool visibleAtBottom =
                IsActive &&
                IsChannelCurrentlySelected(channel) &&
                IsAtBottom(channelView.ScrollViewer);

            bool isRead;

            if (isDebugReplay)
            {
                isRead =
                    !shouldBeImportant;
            }
            else if (message.IsOutgoing)
            {
                isRead = true;
                shouldBeImportant = false;
            }
            else if (shouldBeImportant)
            {
                isRead =
                    visibleAtBottom;
            }
            else
            {
                isRead =
                    preference.AutoMarkRead ||
                    visibleAtBottom;
            }

            ChatMessageState state = new()
            {
                Record = message,
                IsRead = isRead,
                IsImportant =
                    shouldBeImportant &&
                    !isRead
            };

            _messages.Add(state);

            if (persist)
            {
                AppendPersistedMessage(message);
            }

            RenderMessageState(state);

            if (IsChannelCurrentlySelected(channel) &&
                IsActive)
            {
                channelView.ScrollViewer
                    .ScrollToEnd();
            }

            bool firedImportantAlert =
                shouldBeImportant &&
                !message.IsOutgoing &&
                !suppressSound;

            RefreshUnreadState();

            if (firedImportantAlert)
            {
                try
                {
                    global::System.Media.SystemSounds
                        .Exclamation
                        .Play();
                }
                catch
                {
                    // A missing or disabled Windows sound must not
                    // interrupt chat processing.
                }
            }
        }

        private void LoadPersistedHistory()
        {
            _messages.Clear();
            ResetChannelViewsForIdentity();

            if (string.IsNullOrWhiteSpace(
                    _historyFilePath) ||
                !File.Exists(
                    _historyFilePath))
            {
                RefreshUnreadState();
                SetStatus(
                    "No saved chat history exists yet for this character.");
                return;
            }

            int loadedCount = 0;
            int failedCount = 0;

            try
            {
                foreach (string line in
                         File.ReadLines(
                             _historyFilePath))
                {
                    if (string.IsNullOrWhiteSpace(
                            line))
                    {
                        continue;
                    }

                    try
                    {
                        InstantMessageRecord? message =
                            JsonSerializer.Deserialize<
                                InstantMessageRecord>(
                                line,
                                JsonOptions);

                        if (message == null ||
                            string.IsNullOrWhiteSpace(
                                message.Channel))
                        {
                            failedCount++;
                            continue;
                        }

                        message.Channel =
                            NormalizeChannelName(
                                message.Channel);

                        EnsureChannelView(
                            message.Channel);

                        ChatMessageState state = new()
                        {
                            Record = message,
                            IsRead = true,
                            IsImportant = false
                        };

                        _messages.Add(state);

                        if (!GetPreference(
                                message.Channel)
                            .IgnoreAll)
                        {
                            RenderMessageState(state);
                        }

                        loadedCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Unable to load chat history: {ex.Message}");
                RefreshUnreadState();
                return;
            }

            RefreshUnreadState();

            string failureText =
                failedCount > 0
                    ? $" ({failedCount:N0} invalid lines skipped)"
                    : string.Empty;

            SetStatus(
                $"Loaded {loadedCount:N0} saved messages as read{failureText}.");

            if (_hasLoaded)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(
                        () =>
                        {
                            foreach (ChannelView view in
                                     _channelViews.Values)
                            {
                                view.ScrollViewer
                                    .ScrollToEnd();
                            }
                        }));
            }
        }

        private void AppendPersistedMessage(
            InstantMessageRecord message)
        {
            if (string.IsNullOrWhiteSpace(
                    _historyFilePath))
            {
                return;
            }

            try
            {
                string? directory =
                    Path.GetDirectoryName(
                        _historyFilePath);

                if (!string.IsNullOrWhiteSpace(
                        directory))
                {
                    Directory.CreateDirectory(
                        directory);
                }

                string json =
                    JsonSerializer.Serialize(
                        message,
                        JsonOptions);

                File.AppendAllText(
                    _historyFilePath,
                    json +
                    Environment.NewLine);
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Unable to save chat history: {ex.Message}");
            }
        }

        private ChannelView EnsureAllView()
        {
            if (_channelViews.TryGetValue(
                    AllChannelName,
                    out ChannelView? existing))
            {
                return existing;
            }

            ChannelView view =
                CreateChannelView(
                    AllChannelName,
                    showOptions: false);

            _channelViews[AllChannelName] =
                view;

            ChannelTabs.Items.Insert(
                0,
                view.TabItem);

            if (ChannelTabs.SelectedItem == null)
            {
                ChannelTabs.SelectedItem =
                    view.TabItem;
            }

            return view;
        }

        private ChannelView EnsureChannelView(
            string channelName)
        {
            string normalized =
                NormalizeChannelName(
                    channelName);

            if (_channelViews.TryGetValue(
                    normalized,
                    out ChannelView? existing))
            {
                return existing;
            }

            ChannelView view =
                CreateChannelView(
                    normalized,
                    showOptions: true);

            _channelViews[normalized] =
                view;
            ChannelTabs.Items.Add(
                view.TabItem);

            return view;
        }

        private ChannelView CreateChannelView(
            string channelName,
            bool showOptions)
        {
            StackPanel messagesPanel = new()
            {
                Margin =
                    new Thickness(
                        8,
                        8,
                        8,
                        12)
            };

            ScrollViewer scrollViewer = new()
            {
                Content = messagesPanel,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            24,
                            27,
                            32)),
                Tag = channelName
            };
            scrollViewer.ScrollChanged +=
                ScrollViewer_ScrollChanged;

            DockPanel content = new();

            if (showOptions)
            {
                FrameworkElement options =
                    CreateChannelOptions(
                        channelName);
                DockPanel.SetDock(
                    options,
                    Dock.Top);
                content.Children.Add(
                    options);
            }

            content.Children.Add(
                scrollViewer);

            Border colorDot = new()
            {
                Width = 8,
                Height = 8,
                CornerRadius =
                    new CornerRadius(4),
                Margin =
                    new Thickness(
                        0,
                        0,
                        5,
                        0),
                Background =
                    GetChannelBrush(
                        channelName),
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            TextBlock nameText = new()
            {
                Text = channelName,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            232,
                            236,
                            241)),
                FontSize = 11,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            TextBlock unreadText = new()
            {
                Margin =
                    new Thickness(
                        5,
                        0,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            152,
                            205,
                            255)),
                FontSize = 10,
                FontWeight =
                    FontWeights.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            TextBlock importantText = new()
            {
                Text = "!",
                Margin =
                    new Thickness(
                        4,
                        0,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            92,
                            92)),
                FontWeight =
                    FontWeights.Bold,
                FontSize = 13,
                Opacity = 0,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            StackPanel header = new()
            {
                Orientation =
                    Orientation.Horizontal,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
            header.Children.Add(
                colorDot);
            header.Children.Add(
                nameText);
            header.Children.Add(
                unreadText);
            header.Children.Add(
                importantText);

            TabItem tabItem = new()
            {
                Header = header,
                Content = content,
                Tag = channelName
            };

            return new ChannelView
            {
                ChannelName = channelName,
                TabItem = tabItem,
                ScrollViewer = scrollViewer,
                MessagesPanel = messagesPanel,
                UnreadText = unreadText,
                ImportantText = importantText
            };
        }

        private FrameworkElement CreateChannelOptions(
            string channelName)
        {
            InstantMessengerChannelPreference preference =
                GetPreference(channelName);

            CheckBox autoRead = CreateOptionCheckBox(
                "Auto mark as read",
                preference.AutoMarkRead,
                "New non-important messages for this channel do not add to unread.");

            CheckBox autoImportant = CreateOptionCheckBox(
                "Auto mark as important",
                preference.AutoMarkImportant,
                "Every new incoming message in this channel fires the important alert.");

            CheckBox markNameImportant = CreateOptionCheckBox(
                "Mark my name as important",
                preference.MarkMyNameImportant,
                $"Messages containing {_characterName} are important.");

            CheckBox ignoreAll = CreateOptionCheckBox(
                "Ignore all",
                preference.IgnoreAll,
                "Disregard this channel and exclude it from the All tab.");

            autoRead.Checked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.AutoMarkRead = true,
                        markExistingRead: true);
            autoRead.Unchecked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.AutoMarkRead = false);

            autoImportant.Checked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.AutoMarkImportant = true);
            autoImportant.Unchecked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.AutoMarkImportant = false);

            markNameImportant.Checked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.MarkMyNameImportant = true);
            markNameImportant.Unchecked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.MarkMyNameImportant = false);

            ignoreAll.Checked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.IgnoreAll = true,
                        markExistingRead: true,
                        rebuildViews: true);
            ignoreAll.Unchecked +=
                (_, _) =>
                    UpdatePreference(
                        channelName,
                        preferenceItem =>
                            preferenceItem.IgnoreAll = false,
                        rebuildViews: true);

            WrapPanel optionsPanel = new()
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0)
            };
            optionsPanel.Children.Add(
                autoRead);
            optionsPanel.Children.Add(
                autoImportant);
            optionsPanel.Children.Add(
                markNameImportant);
            optionsPanel.Children.Add(
                ignoreAll);

            TextBlock heading = new()
            {
                Text =
                    $"{channelName} channel settings",
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            238,
                            242,
                            246)),
                FontSize = 11,
                FontWeight =
                    FontWeights.SemiBold
            };

            TextBlock description = new()
            {
                Text =
                    "These options are saved separately for this channel.",
                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            151,
                            161,
                            172)),
                FontSize = 9
            };

            StackPanel settingsContent = new()
            {
                Margin =
                    new Thickness(
                        10,
                        7,
                        10,
                        7)
            };
            settingsContent.Children.Add(
                heading);
            settingsContent.Children.Add(
                description);
            settingsContent.Children.Add(
                optionsPanel);

            return new Border
            {
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            32,
                            37,
                            43)),
                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            67,
                            75,
                            85)),
                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),
                Child = settingsContent
            };
        }


        private static CheckBox CreateOptionCheckBox(
            string text,
            bool isChecked,
            string toolTip)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin =
                    new Thickness(
                        0,
                        0,
                        22,
                        6),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            225,
                            229,
                            234)),
                ToolTip = toolTip,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        }

        private void UpdatePreference(
            string channelName,
            Action<InstantMessengerChannelPreference>
                update,
            bool markExistingRead = false,
            bool rebuildViews = false)
        {
            if (_isApplyingPreferenceControls)
            {
                return;
            }

            InstantMessengerChannelPreference preference =
                GetPreference(channelName);

            update(preference);

            if (markExistingRead)
            {
                foreach (ChatMessageState state in
                         _messages.Where(
                             state =>
                                 state.Record.Channel.Equals(
                                     channelName,
                                     StringComparison.OrdinalIgnoreCase)))
                {
                    state.IsRead = true;
                    state.IsImportant = false;
                    RefreshMessageVisuals(
                        state);
                }
            }

            if (rebuildViews)
            {
                RebuildRenderedMessages();
            }

            ChannelSettingsChanged?.Invoke();
            RefreshUnreadState();
        }

        private InstantMessengerChannelPreference GetPreference(
            string channelName)
        {
            string normalized =
                NormalizeChannelName(
                    channelName);

            if (_channelPreferences.TryGetValue(
                    normalized,
                    out InstantMessengerChannelPreference? existing))
            {
                return existing;
            }

            InstantMessengerChannelPreference preference =
                CreateDefaultPreference(
                    normalized);

            _channelPreferences[normalized] =
                preference;

            ChannelSettingsChanged?.Invoke();

            return preference;
        }

        private static InstantMessengerChannelPreference
            CreateDefaultPreference(
                string channelName)
        {
            bool autoRead =
                channelName.Equals(
                    "General",
                    StringComparison.OrdinalIgnoreCase) ||
                channelName.Equals(
                    "New Players",
                    StringComparison.OrdinalIgnoreCase) ||
                channelName.Equals(
                    SayChannelName,
                    StringComparison.OrdinalIgnoreCase);

            bool autoImportant =
                channelName.Equals(
                    "Guild",
                    StringComparison.OrdinalIgnoreCase) ||
                channelName.Equals(
                    "Group",
                    StringComparison.OrdinalIgnoreCase) ||
                channelName.Equals(
                    "Tells",
                    StringComparison.OrdinalIgnoreCase);

            return new InstantMessengerChannelPreference
            {
                AutoMarkRead = autoRead,
                AutoMarkImportant = autoImportant,
                MarkMyNameImportant = true,
                IgnoreAll = false
            };
        }

        private void RenderMessageState(
            ChatMessageState state)
        {
            string channel =
                state.Record.Channel;

            if (GetPreference(channel)
                .IgnoreAll)
            {
                return;
            }

            ChannelView channelView =
                EnsureChannelView(channel);
            ChannelView allView =
                EnsureAllView();

            Border channelBubble =
                CreateMessageBubble(
                    state,
                    includeChannelLabel: false);
            channelView.MessagesPanel.Children.Add(
                channelBubble);
            state.Bubbles.Add(
                channelBubble);

            Border allBubble =
                CreateMessageBubble(
                    state,
                    includeChannelLabel: true);
            allView.MessagesPanel.Children.Add(
                allBubble);
            state.Bubbles.Add(
                allBubble);
        }

        private Border CreateMessageBubble(
            ChatMessageState state,
            bool includeChannelLabel)
        {
            InstantMessageRecord record =
                state.Record;

            TextBlock metadata = new()
            {
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            185,
                            192,
                            201)),
                FontSize = 10,
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

            string channelPrefix =
                includeChannelLabel
                    ? $"[{record.Channel}] "
                    : string.Empty;

            string recipientText =
                !string.IsNullOrWhiteSpace(
                    record.Recipient)
                    ? $" → {record.Recipient}"
                    : string.Empty;

            metadata.Text =
                $"{record.Timestamp:HH:mm:ss}  " +
                channelPrefix +
                $"{record.Sender}{recipientText}";

            TextBlock body = new()
            {
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            247,
                            249,
                            251)),
                FontSize = 12,
                TextWrapping =
                    TextWrapping.Wrap,
                Text = record.Text
            };

            StackPanel content = new();
            content.Children.Add(
                metadata);
            content.Children.Add(
                body);

            Border bubble = new()
            {
                MaxWidth = 610,
                HorizontalAlignment =
                    record.IsOutgoing
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left,
                Margin =
                    new Thickness(
                        record.IsOutgoing
                            ? 90
                            : 0,
                        0,
                        record.IsOutgoing
                            ? 0
                            : 90,
                        8),
                Padding =
                    new Thickness(
                        10,
                        7,
                        10,
                        8),
                CornerRadius =
                    new CornerRadius(9),
                Child = content,
                Tag = state
            };

            ApplyBubbleVisual(
                bubble,
                state);

            return bubble;
        }

        private void ApplyBubbleVisual(
            Border bubble,
            ChatMessageState state)
        {
            Color channelColor =
                GetChannelColor(
                    state.Record.Channel);

            byte backgroundAlpha =
                state.IsImportant
                    ? (byte)92
                    : state.IsRead
                        ? (byte)42
                        : (byte)70;

            bubble.Background =
                new SolidColorBrush(
                    Color.FromArgb(
                        backgroundAlpha,
                        channelColor.R,
                        channelColor.G,
                        channelColor.B));

            bubble.BorderBrush =
                state.IsImportant
                    ? new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            92,
                            92))
                    : new SolidColorBrush(
                        Color.FromArgb(
                            state.IsRead
                                ? (byte)90
                                : (byte)190,
                            channelColor.R,
                            channelColor.G,
                            channelColor.B));

            bubble.BorderThickness =
                new Thickness(
                    state.IsImportant
                        ? 2
                        : 1);

            bubble.Opacity =
                state.IsRead
                    ? 0.82
                    : 1;
        }

        private void RefreshMessageVisuals(
            ChatMessageState state)
        {
            foreach (Border bubble in
                     state.Bubbles)
            {
                ApplyBubbleVisual(
                    bubble,
                    state);
            }
        }

        private void ResetChannelViewsForIdentity()
        {
            List<TabItem> channelTabs =
                _channelViews
                    .Where(pair =>
                        !pair.Key.Equals(
                            AllChannelName,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(pair =>
                        pair.Value.TabItem)
                    .ToList();

            foreach (TabItem tab in
                     channelTabs)
            {
                ChannelTabs.Items.Remove(
                    tab);
            }

            foreach (string channel in
                     _channelViews.Keys
                         .Where(channel =>
                             !channel.Equals(
                                 AllChannelName,
                                 StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _channelViews.Remove(
                    channel);
            }

            ChannelView allView =
                EnsureAllView();
            allView.MessagesPanel.Children.Clear();
            allView.UnreadCount = 0;
            allView.ImportantCount = 0;
            ChannelTabs.SelectedItem =
                allView.TabItem;
        }

        private void ClearRenderedMessages()
        {
            foreach (ChannelView view in
                     _channelViews.Values)
            {
                view.MessagesPanel.Children.Clear();
                view.UnreadCount = 0;
                view.ImportantCount = 0;
            }

            foreach (ChatMessageState state in
                     _messages)
            {
                state.Bubbles.Clear();
            }
        }

        private void RebuildRenderedMessages()
        {
            ClearRenderedMessages();

            foreach (ChatMessageState state in
                     _messages)
            {
                if (!GetPreference(
                        state.Record.Channel)
                    .IgnoreAll)
                {
                    RenderMessageState(
                        state);
                }
            }
        }

        private void MarkChannelRead(
            string channelName)
        {
            bool markAll =
                channelName.Equals(
                    AllChannelName,
                    StringComparison.OrdinalIgnoreCase);

            foreach (ChatMessageState state in
                     _messages)
            {
                if (!markAll &&
                    !state.Record.Channel.Equals(
                        channelName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (GetPreference(
                        state.Record.Channel)
                    .IgnoreAll)
                {
                    continue;
                }

                if (!state.IsRead ||
                    state.IsImportant)
                {
                    state.IsRead = true;
                    state.IsImportant = false;
                    RefreshMessageVisuals(
                        state);
                }
            }

            RefreshUnreadState();
        }

        private void RefreshUnreadState()
        {
            ChannelView allView =
                EnsureAllView();

            foreach (ChannelView view in
                     _channelViews.Values)
            {
                view.UnreadCount = 0;
                view.ImportantCount = 0;
            }

            foreach (ChatMessageState state in
                     _messages)
            {
                if (GetPreference(
                        state.Record.Channel)
                    .IgnoreAll)
                {
                    continue;
                }

                if (state.IsRead)
                {
                    continue;
                }

                allView.UnreadCount++;

                if (state.IsImportant)
                {
                    allView.ImportantCount++;
                }

                if (_channelViews.TryGetValue(
                        state.Record.Channel,
                        out ChannelView? channelView))
                {
                    channelView.UnreadCount++;

                    if (state.IsImportant)
                    {
                        channelView.ImportantCount++;
                    }
                }
            }

            foreach (ChannelView view in
                     _channelViews.Values)
            {
                view.UnreadText.Text =
                    view.UnreadCount > 0
                        ? $"({view.UnreadCount:N0})"
                        : string.Empty;

                view.ImportantText.Opacity =
                    view.ImportantCount > 0 &&
                    _blinkVisible
                        ? 1
                        : 0;
            }

            int unreadCount =
                allView.UnreadCount;
            bool hasImportant =
                allView.ImportantCount > 0;

            SummaryText.Text =
                unreadCount <= 0
                    ? "No unread messages"
                    : hasImportant
                        ? $"{unreadCount:N0} unread — important messages waiting"
                        : $"{unreadCount:N0} unread messages";

            UnreadStateChanged?.Invoke(
                unreadCount,
                hasImportant);
        }

        private bool IsChannelCurrentlySelected(
            string channelName)
        {
            if (ChannelTabs.SelectedItem is not
                    TabItem selected ||
                selected.Tag is not
                    string selectedChannel)
            {
                return false;
            }

            return selectedChannel.Equals(
                       AllChannelName,
                       StringComparison.OrdinalIgnoreCase) ||
                   selectedChannel.Equals(
                       channelName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAtBottom(
            ScrollViewer viewer)
        {
            return viewer.ScrollableHeight <= 0 ||
                   viewer.VerticalOffset >=
                   viewer.ScrollableHeight - 2;
        }

        private static string BuildHistoryFilePath(
            string characterName,
            string serverName)
        {
            string safeCharacter =
                MakeSafeFilePart(
                    characterName);
            string safeServer =
                MakeSafeFilePart(
                    serverName);

            return Path.Combine(
                AppContext.BaseDirectory,
                "ChatLogs",
                $"{safeCharacter}_{safeServer}.chat.jsonl");
        }

        private static string MakeSafeFilePart(
            string value)
        {
            HashSet<char> invalid =
                Path.GetInvalidFileNameChars()
                    .ToHashSet();

            string sanitized =
                new(
                    (value ?? string.Empty)
                    .Select(character =>
                        invalid.Contains(character)
                            ? '_'
                            : character)
                    .ToArray());

            sanitized =
                sanitized.Trim()
                    .TrimEnd('.');

            return string.IsNullOrWhiteSpace(
                    sanitized)
                ? "Unknown"
                : sanitized;
        }

        private static string NormalizeChannelName(
            string channelName)
        {
            string normalized =
                string.IsNullOrWhiteSpace(
                    channelName)
                    ? "Other"
                    : channelName.Trim();

            normalized =
                global::System.Text.RegularExpressions.Regex
                    .Replace(
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
                    "groupsay",
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "group",
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
                    StringComparison.OrdinalIgnoreCase) ||
                compact.Equals(
                    "you",
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

        private static Brush GetChannelBrush(
            string channelName)
        {
            Color color =
                GetChannelColor(
                    channelName);

            return new SolidColorBrush(
                color);
        }

        private static Color GetChannelColor(
            string channelName)
        {
            if (channelName.Equals(
                    "Tells",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    255,
                    112,
                    210);
            }

            if (channelName.Equals(
                    "Guild",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    174,
                    122,
                    255);
            }

            if (channelName.Equals(
                    "Group",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    104,
                    224,
                    139);
            }

            if (channelName.Equals(
                    "Say",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    245,
                    214,
                    106);
            }

            if (channelName.Equals(
                    "General",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    93,
                    174,
                    255);
            }

            if (channelName.Equals(
                    "New Players",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    72,
                    220,
                    224);
            }

            if (channelName.Equals(
                    "Auction",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    255,
                    165,
                    73);
            }

            if (channelName.Equals(
                    "Shout",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    255,
                    87,
                    87);
            }

            if (channelName.Equals(
                    "Raid",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    232,
                    92,
                    124);
            }

            if (channelName.Equals(
                    "Fellowship",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    69,
                    203,
                    181);
            }

            if (channelName.Equals(
                    "OOC",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    171,
                    180,
                    190);
            }

            if (channelName.Equals(
                    AllChannelName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Color.FromRgb(
                    203,
                    210,
                    218);
            }

            unchecked
            {
                uint hash =
                    (uint)StringComparer.OrdinalIgnoreCase
                        .GetHashCode(
                            channelName);

                byte red =
                    (byte)(90 +
                           hash % 140);
                byte green =
                    (byte)(90 +
                           (hash / 17) % 140);
                byte blue =
                    (byte)(90 +
                           (hash / 31) % 140);

                return Color.FromRgb(
                    red,
                    green,
                    blue);
            }
        }

        private void SetStatus(
            string text)
        {
            StatusText.Text = text;
        }

        private sealed class ChatMessageState
        {
            public InstantMessageRecord Record { get; set; } =
                new();
            public bool IsRead { get; set; }
            public bool IsImportant { get; set; }
            public List<Border> Bubbles { get; } =
                new();
        }

        private sealed class ChannelView
        {
            public string ChannelName { get; set; } =
                string.Empty;
            public TabItem TabItem { get; set; } =
                new();
            public ScrollViewer ScrollViewer { get; set; } =
                new();
            public StackPanel MessagesPanel { get; set; } =
                new();
            public TextBlock UnreadText { get; set; } =
                new();
            public TextBlock ImportantText { get; set; } =
                new();
            public int UnreadCount { get; set; }
            public int ImportantCount { get; set; }
        }
    }
}
