using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CareerCompanion.Core.Providers.Fifa18;

namespace CareerCompanion.App;

/// <summary>One entry in the Ctrl+K palette: a place to go or a thing to do.</summary>
public sealed record PaletteCommand(string Title, string Hint, string Glyph, Action Run);

public partial class MainWindow : Window
{
    private readonly Database _db;
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    private readonly List<PaletteCommand> _commands = [];
    private Fifa18SaveWatcher? _fifaWatcher;
    private bool _fifaScanBusy;
    private bool _fifaRescanRequested;
    private bool _fifaManualRescanRequested;
    private int _fifaWatcherRecoveryAttempt;
    private bool _openingFifaReview;

    public MainWindow()
    {
        InitializeComponent();
        var overrideRoot = Environment.GetEnvironmentVariable("TOUCHLINE_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchlineCareerCompanion")
            : Path.GetFullPath(overrideRoot);
        var db = new Database(Path.Combine(root, "career-world.db"));
        db.Migrate();
        new DemoSeeder(db).EnsureDemo();
        _db = db;
        _vm = new MainViewModel(db);
        DataContext = _vm;

        IndexPages();
        BuildCommands();

        // The status line is the app's running commentary; surface it as a toast instead of
        // making the player watch a corner of the sidebar.
        _vm.PropertyChanged += OnViewModelChanged;
        ((INotifyCollectionChanged)_vm.Messages).CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(MessageScroll.ScrollToEnd));

        Loaded += async (_, _) =>
        {
            ApplyDarkTitleBar();
            RestartFifaWatcher();
            if (_vm.AutoFifaWatch && Directory.Exists(_vm.FifaSettingsDirectory)) await ScanFifaAsync(true);
        };
        Closed += (_, _) => { _fifaWatcher?.Dispose(); _db.Checkpoint(); };
    }

    // ---------------------------------------------------------------- navigation

    /// <summary>
    /// Maps each navigable item's Tag to its page. Navigation is by name from here on, so adding
    /// a section never means renumbering the call sites that jump to one.
    /// </summary>
    private void IndexPages()
    {
        foreach (var item in Navigation.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not string key) continue;
            if (FindName(key + "Page") is FrameworkElement page) _pages[key] = page;
        }
    }

    private string? CurrentPageKey => (Navigation.SelectedItem as ListBoxItem)?.Tag as string;

    private void Navigate(string key)
    {
        var item = Navigation.Items.OfType<ListBoxItem>().FirstOrDefault(x => (x.Tag as string) == key);
        if (item is not null) Navigation.SelectedItem = item;
    }

    private void Navigation_Changed(object sender, SelectionChangedEventArgs e) => Guard(() =>
    {
        var key = CurrentPageKey;
        if (key is null || _pages.Count == 0) return;
        foreach (var (name, page) in _pages)
        {
            if (name == key) { page.Visibility = Visibility.Visible; AnimatePageIn(page); }
            else page.Visibility = Visibility.Collapsed;
        }

        // Opening the match form fresh, unless we arrived here to finish a FIFA review.
        if (key == "Match")
        {
            if (!_openingFifaReview) _vm.StartNewManualMatch();
            _openingFifaReview = false;
        }
        if (key == "Debug") _vm.Refresh();
        if (key == "Messages") Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(MessageScroll.ScrollToEnd));
    });

    /// <summary>A short rise and fade so switching sections reads as a move, not a flicker.</summary>
    private static void AnimatePageIn(FrameworkElement page)
    {
        var slide = new TranslateTransform();
        page.RenderTransform = slide;
        page.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ---------------------------------------------------------------- shell chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Asks Windows for a dark caption in the app's own colour. Keeping the native title bar keeps
    /// snap layouts and every other window behaviour the OS provides; only the paint changes.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var useDark = 1;
            DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int));      // immersive dark mode
            var caption = 0x2B0C0F;                                            // COLORREF for #0F0C2B
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));       // caption colour
            var border = 0x793E36;                                             // COLORREF for #363E79
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));        // border colour
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // ---------------------------------------------------------------- transient feedback

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status)) ShowToast(_vm.Status);
    }

    /// <summary>Slides a short confirmation into the top-right corner and takes it away again.</summary>
    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var slide = new TranslateTransform(0, -8);
        var toast = new Border
        {
            Background = (Brush)FindResource("Panel2"),
            BorderBrush = (Brush)FindResource("Interactive"),
            BorderThickness = new Thickness(0, 0, 3, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 11, 18, 11),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
            Opacity = 0,
            RenderTransform = slide,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 5, Opacity = 0.45 },
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = (Brush)FindResource("Text")
            }
        };

        ToastHost.Children.Add(toast);
        if (ToastHost.Children.Count > 3) ToastHost.Children.RemoveAt(0);

        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _) => ToastHost.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- command palette

    private void BuildCommands()
    {
        void Go(string key, string title, string glyph) =>
            _commands.Add(new(title, "Go to", glyph, () => Navigate(key)));

        Go("Home", "Home", "");
        Go("WorldInbox", "World updates", "");
        Go("PreMatch", "Pre-match briefing", "");
        Go("ReviewInbox", "Review inbox", "");
        Go("Squad", "Squad", "");
        Go("Messages", "Messages", "");
        Go("Manager", "Manager's office", "");
        Go("Press", "Press room", "");
        Go("News", "News", "");
        Go("Social", "Social", "");
        Go("Timeline", "Career timeline", "");
        Go("Match", "Match centre", "");
        Go("Career", "Career setup", "");
        Go("Settings", "Settings", "");

        _commands.Add(new("Scan the newest FIFA save", "Action", "",
            () => _ = ScanFifaAsync(false)));
        _commands.Add(new("Log a new match by hand", "Action", "",
            () => { Navigate("Match"); Guard(_vm.StartNewManualMatch); }));
        _commands.Add(new("Talk to the manager", "Action", "", () => TalkManager_Click(this, new())));
        _commands.Add(new("Talk to a teammate", "Action", "", () => TalkTeammate_Click(this, new())));
        _commands.Add(new("Contact your agent", "Action", "", () => TalkAgent_Click(this, new())));
        _commands.Add(new("Mark all world updates read", "Action", "", () => Guard(_vm.MarkAllNotificationsRead)));
        _commands.Add(new("Export a backup", "Action", "", () => Backup_Click(this, new())));
        _commands.Add(new("Restore a backup", "Action", "", () => Restore_Click(this, new())));
    }

    private void OpenPalette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    private void OpenPalette()
    {
        PaletteInput.Text = "";
        FilterPalette();
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        PaletteInput.Focus();
    }

    private void ClosePalette_Click(object sender, RoutedEventArgs e) => ClosePalette();

    private void ClosePalette() => PaletteOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Clicks inside the palette card must not fall through to the dismissing scrim.</summary>
    private void SwallowClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void PaletteInput_TextChanged(object sender, TextChangedEventArgs e) => FilterPalette();

    private void FilterPalette()
    {
        var term = PaletteInput.Text.Trim();
        PaletteResults.ItemsSource = string.IsNullOrEmpty(term)
            ? _commands
            : _commands.Where(x => x.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        if (PaletteResults.Items.Count > 0) PaletteResults.SelectedIndex = 0;
    }

    private void PaletteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: ClosePalette(); e.Handled = true; break;
            case Key.Enter: RunPaletteSelection(); e.Handled = true; break;
            case Key.Down:
                PaletteResults.SelectedIndex = Math.Min(PaletteResults.SelectedIndex + 1, PaletteResults.Items.Count - 1);
                e.Handled = true; break;
            case Key.Up:
                PaletteResults.SelectedIndex = Math.Max(PaletteResults.SelectedIndex - 1, 0);
                e.Handled = true; break;
        }
    }

    private void RunPaletteSelection_Click(object sender, RoutedEventArgs e) => RunPaletteSelection();

    private void RunPaletteSelection()
    {
        if (PaletteResults.SelectedItem is not PaletteCommand command) return;
        ClosePalette();
        Guard(command.Run);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.K) { OpenPalette(); e.Handled = true; }
        else if (e.Key == Key.Escape && PaletteOverlay.Visibility == Visibility.Visible)
        { ClosePalette(); e.Handled = true; }
        else if (e.Key == Key.F5) { _ = ScanFifaAsync(false); e.Handled = true; }
        else if (control && e.Key is >= Key.D1 and <= Key.D9)
        {
            var wanted = e.Key - Key.D1;
            var destinations = Navigation.Items.OfType<ListBoxItem>()
                .Where(x => x.Tag is string && x.Visibility == Visibility.Visible).ToList();
            if (wanted < destinations.Count) Navigation.SelectedItem = destinations[wanted];
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // ---------------------------------------------------------------- actions

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Touchline", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void GoCareer_Click(object sender, RoutedEventArgs e) => Navigate("Career");
    private void GoPreMatch_Click(object sender, RoutedEventArgs e) => Navigate("PreMatch");
    private void GoMessages_Click(object sender, RoutedEventArgs e) => Navigate("Messages");
    private void GoNews_Click(object sender, RoutedEventArgs e) => Navigate("News");

    private void MessageSelectedPlayer_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (_vm.SelectedSquadMember is null) { MessageBox.Show("Select a player first.", "Touchline"); return; }
        _vm.SelectedCharacter = _vm.SelectedSquadMember.Character;
        Navigate("Messages");
        ComposerBox.Focus();
    });

    private void CreateCareer_Click(object sender, RoutedEventArgs e) => Guard(_vm.CreateCareer);
    private void AddCharacter_Click(object sender, RoutedEventArgs e) => Guard(_vm.AddCharacter);

    private void ProcessMatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.ProcessMatch();
            if (_vm.HasActiveInterview) { _vm.MarkActiveInterviewRead(); Navigate("Press"); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Touchline", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenUp_Click(object sender, RoutedEventArgs e)
    {
        try { if (_vm.ChooseRecovery("open_up")) Navigate("Messages"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Touchline", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RecoveryDay_Click(object sender, RoutedEventArgs e) => Guard(() => _vm.ChooseRecovery("recover"));
    private void TrainingReset_Click(object sender, RoutedEventArgs e) => Guard(() => _vm.ChooseRecovery("training"));
    private void EditMatch_Click(object sender, RoutedEventArgs e) => Guard(_vm.EditSelectedMatch);
    private void NewManualMatch_Click(object sender, RoutedEventArgs e) => Guard(_vm.StartNewManualMatch);
    private void CancelMatchEdit_Click(object sender, RoutedEventArgs e) => Guard(_vm.CancelMatchEdit);

    private void DeleteMatch_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLoggedMatch is null) { MessageBox.Show("Select a logged match first.", "Touchline"); return; }
        var match = _vm.SelectedLoggedMatch.Input;
        if (MessageBox.Show(
                $"Delete the {match.TeamScore}-{match.OpponentScore} match against {match.Opponent} and its generated world activity?",
                "Delete match", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Guard(_vm.DeleteSelectedMatch);
    }

    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        SendMessage_Click(sender, e);
    }

    private async void SendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.MessageText)) return;
        try
        {
            using (_vm.Busy("Waiting for a reply..."))
                await _vm.SendMessageAsync();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(MessageScroll.ScrollToEnd));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Touchline", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void RecordPress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using (_vm.Busy("The journalist is responding..."))
                await _vm.RecordPressAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Post-match interview", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DeclineInterview_Click(object sender, RoutedEventArgs e) => Guard(_vm.DeclineInterview);

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        Guard(() => _vm.SaveSettings(ApiKeyBox.Password));
        RestartFifaWatcher();
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Touchline backup (*.db)|*.db",
            FileName = $"touchline-backup-{DateTime.Now:yyyyMMdd-HHmm}.db"
        };
        if (dialog.ShowDialog() == true) Guard(() => _vm.Backup(dialog.FileName));
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCharacter is null)
        {
            MessageBox.Show("Select a character in Squad first.", "Touchline");
            Navigate("Squad");
            return;
        }
        var dialog = new ProfileEditorWindow(_vm.SelectedCharacter) { Owner = this };
        if (dialog.ShowDialog() == true) { Guard(dialog.Save); _vm.Refresh(); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Touchline backup (*.db)|*.db" };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Restore this backup? Current local data will be replaced. Export a backup first if needed.",
                "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Guard(() => _vm.Restore(dialog.FileName));
    }

    // ---------------------------------------------------------------- FIFA synchronization

    private async void ScanFifa_Click(object sender, RoutedEventArgs e) => await ScanFifaAsync(false);
    private void AutoFifaWatch_Click(object sender, RoutedEventArgs e) => Guard(() => { _vm.SaveSettings(null); RestartFifaWatcher(); });
    private void FifaPreference_Click(object sender, RoutedEventArgs e) => Guard(() => _vm.SaveSettings(null));

    private async Task ScanFifaAsync(bool automatic)
    {
        if (_fifaScanBusy)
        {
            _fifaRescanRequested = true;
            if (!automatic) _fifaManualRescanRequested = true;
            return;
        }
        _fifaScanBusy = true;
        var busy = _vm.Busy(automatic ? "Reading a stable save snapshot..." : "Scanning the newest FIFA 18 save...");
        try
        {
            var result = await _vm.ScanFifaSaveAsync(automatic);
            _fifaWatcherRecoveryAttempt = 0;
            if (automatic
                && result.Disposition is Fifa18ScanDisposition.CareerMismatch or Fifa18ScanDisposition.NoCareerSelected
                && _vm.CanAutoLinkPendingFifa)
            {
                _vm.CreateCareerFromPendingFifa();
                Navigate("WorldInbox");
            }
            else if (!automatic
                && result.Disposition is Fifa18ScanDisposition.CareerMismatch or Fifa18ScanDisposition.NoCareerSelected)
            {
                var answer = MessageBox.Show(
                    $"The FIFA save belongs to {result.Parsed.PlayerName} at {result.Parsed.ClubName}. Create and link a companion career for it?",
                    "Link FIFA career", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes) { _vm.CreateCareerFromPendingFifa(); Navigate("ReviewInbox"); }
            }
            else if (result.Disposition == Fifa18ScanDisposition.MatchDetected && !automatic)
            {
                Navigate("ReviewInbox");
            }
        }
        catch (Exception ex)
        {
            _vm.MarkFifaSyncFailure(ex.Message);
            if (!automatic) MessageBox.Show(ex.Message, "FIFA 18 sync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            busy.Dispose();
            _fifaScanBusy = false;
            if (_fifaRescanRequested)
            {
                var nextAutomatic = !_fifaManualRescanRequested;
                _fifaRescanRequested = false;
                _fifaManualRescanRequested = false;
                if (!nextAutomatic || _vm.AutoFifaWatch)
                    _ = Dispatcher.InvokeAsync(async () => await ScanFifaAsync(nextAutomatic));
            }
        }
    }

    private void ReviewFifaMatch_Click(object sender, RoutedEventArgs e) => Navigate("ReviewInbox");

    private void OpenSelectedReview_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        _vm.PopulatePendingMatchForReview();
        _openingFifaReview = true;
        Navigate("Match");
    });

    private void DismissSelectedReview_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Dismiss this detected match? It will not be offered again.",
                "Dismiss match", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Guard(_vm.DismissSelectedMatchReview);
    }

    private void TalkTeammate_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Teammate,
                CareerCompanion.Core.Domain.SceneType.PreMatch)) Navigate("Messages");
        else MessageBox.Show("No active teammate is available.", "Touchline");
    }

    private void TalkManager_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Manager,
                CareerCompanion.Core.Domain.SceneType.ManagerOffice)) Navigate("Messages");
        else MessageBox.Show("Add a manager character first.", "Touchline");
    }

    private void TalkAgent_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PrepareAgentConversation()) Navigate("Messages");
        else MessageBox.Show("No agent is available in this FIFA career.", "Touchline");
    }

    private void OpenPress_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        _vm.MarkActiveInterviewRead();
        Navigate("Press");
    });

    private void MarkWorldRead_Click(object sender, RoutedEventArgs e) => Guard(_vm.MarkAllNotificationsRead);
    private void ClearWorldRead_Click(object sender, RoutedEventArgs e) => Guard(_vm.ClearReadNotifications);

    private void OpenWorldUpdate_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (_vm.SelectedNotification is null) return;
        var action = _vm.OpenSelectedNotification();
        Navigate(action switch
        {
            "PreMatch" => "PreMatch",
            "Review" => "ReviewInbox",
            "Squad" => "Squad",
            "Messages" => "Messages",
            "Press" => "Press",
            "News" => "News",
            "Social" => "Social",
            "Timeline" => "Timeline",
            _ => "Home"
        });
    });

    private void RestartFifaWatcher()
    {
        _fifaWatcher?.Dispose();
        _fifaWatcher = null;
        if (!_vm.AutoFifaWatch || !Directory.Exists(_vm.FifaSettingsDirectory)) return;

        _fifaWatcher = new Fifa18SaveWatcher(_vm.FifaSettingsDirectory);
        _fifaWatcher.SaveChanged += (_, _) => Dispatcher.InvokeAsync(async () => await ScanFifaAsync(true));
        _fifaWatcher.WatcherError += (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            var delay = Math.Min(30, 1 << Math.Min(5, _fifaWatcherRecoveryAttempt++));
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (!_vm.AutoFifaWatch) return;
            try { RestartFifaWatcher(); await ScanFifaAsync(true); }
            catch (Exception ex) { _vm.MarkFifaSyncFailure(ex.Message); }
        });
    }
}
