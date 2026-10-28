using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClaudeAlerta;

internal sealed class Preferences
{
    public bool Sound { get; set; } = true;
    public bool Popup { get; set; } = true;
    public int ReminderSeconds { get; set; } = 60;
    public string Language { get; set; } = L.DefaultLanguage;
    public static Preferences Load()
    {
        try
        {
            var prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Storage.Root, "preferences.json"))) ?? new();
            prefs.Language = L.Normalize(prefs.Language);
            return prefs;
        }
        catch { return new(); }
    }
    public void Save() => Storage.Atomic(Path.Combine(Storage.Root, "preferences.json"), JsonSerializer.Serialize(this, Storage.Json));
}

internal sealed class SessionRow(SessionState session)
{
    public SessionState Session { get; } = session;
    public string Project => L.Project(Session);
    public string Folder => Session.Folder;
    public bool NeedsAttention => Session.Pending && !Session.Acknowledged;
    public string Status => L.Status(Session.Status) + (Session.Acknowledged ? L.T("SeenSuffix") : "");
    public string AccessibilityLabel => Project + ", " + Status;
    public string Time => Session.Last.ToLocalTime().ToString("HH:mm:ss", L.Culture);
    public string StatusGlyph => Session.Status == "Trabalhando" ? "\uE9F5" : NeedsAttention ? "\uE946" : "\uE73E";
    public Brush StatusBrush => (Brush)new BrushConverter().ConvertFromString(
        SystemParameters.HighContrast ? SystemColors.WindowTextColor.ToString() :
        Session.Status == "Trabalhando" ? "#286247" : NeedsAttention ? "#125FB5" : "#586779")!;
}

public partial class Dashboard : Window
{
    readonly MonitorState state = new();
    readonly Preferences prefs = Preferences.Load();
    readonly ObservableCollection<SessionRow> rows = [];
    readonly Forms.NotifyIcon tray;
    readonly Forms.ContextMenuStrip trayMenu = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly bool diagnostics;
    bool quitting, paused, ready;
    AlertCard? card;
    SessionState? lastAlert;
    DateTimeOffset snoozedUntil;
    DateTimeOffset lastClean;
    internal bool IsPaused => paused;
    internal AlertCard? VisibleCard => card;
    internal string[] TrayLabels => trayMenu.Items.Cast<Forms.ToolStripItem>().Select(item => item.Text ?? "").ToArray();

    internal Dashboard(bool trayOnly, bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        L.Apply(prefs.Language);
        InitializeComponent(); UiTheme.ApplyAccessibility(Resources);
        SessionsList.ItemsSource = rows;
        SoundToggle.IsChecked = prefs.Sound; PopupToggle.IsChecked = prefs.Popup;
        int[] intervals = [0, 30, 60, 120];
        ReminderSelect.SelectedIndex = Math.Max(0, Array.IndexOf(intervals, prefs.ReminderSeconds));
        LanguageSelect.ItemsSource = L.Languages;
        LanguageSelect.SelectedValue = prefs.Language;
        void AddMenu(string key, Action action) { var item = trayMenu.Items.Add(L.T(key), null, (_, _) => action()); item.Tag = key; }
        AddMenu("TrayOpen", Reveal); AddMenu("TestAlert", TestAlert); AddMenu("Snooze", Snooze);
        AddMenu("IntegrationDeactivate", () => ChangeHooks(true)); AddMenu("Exit", Quit);
        var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information;
        tray = new Forms.NotifyIcon { Icon = appIcon, Text = L.T("AppTitle"), Visible = !diagnostics, ContextMenuStrip = trayMenu };
        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        Closing += OnClosing;
        Closed += (_, _) => { timer.Stop(); CloseCard(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); appIcon.Dispose(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        ContentRendered += (_, _) => { if (trayOnly) Hide(); };
        SessionsList.SelectionChanged += (_, _) => UpdateAcknowledgement();
        timer.Tick += (_, _) => Tick();
        ApplyLanguage(); ready = true;
        if (!diagnostics) timer.Start();
    }
    void OnClosing(object? sender, CancelEventArgs e) { if (!quitting) { e.Cancel = true; Hide(); } }
    internal void Quit() { quitting = true; Close(); if (!diagnostics) Application.Current.Shutdown(); }
    void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    void SavePrefs() { try { prefs.Save(); } catch { FooterLabel.Text = L.T("PrefsSaveError"); } }
    void ApplyLanguage()
    {
        bool wasReady = ready; ready = false;
        try
        {
            L.Apply(prefs.Language);
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Current);
            LanguageSelect.SelectedValue = L.Current;
            int reminderIndex = ReminderSelect.SelectedIndex;
            ReminderSelect.SelectedIndex = -1; ReminderSelect.SelectedIndex = reminderIndex;
            foreach (Forms.ToolStripItem item in trayMenu.Items) if (item.Tag is string key) item.Text = L.T(key);
            RenderSessions(); RefreshStatus(); UpdateTrayText(); card?.RefreshLanguage();
        }
        finally { ready = wasReady; }
    }
    void UpdateTrayText() => tray.Text = L.T(state.Sessions.Values.Any(s => s.Pending && !s.Acknowledged) ? "TrayPending" : "AppTitle");
    void ChangeHooks(bool remove)
    {
        try
        {
            HookInstaller.Install(remove); RefreshStatus();
            MessageBox.Show(this, L.T(remove ? "IntegrationRemoved" : "IntegrationAdded"),
                L.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            string message = ex is JsonException ? L.T("ConfigInvalid") :
                ex is IOException or UnauthorizedAccessException && ex is not InvalidDataException ? L.T("ConfigWriteError") : ex.Message;
            MessageBox.Show(this, message, L.T("IntegrationErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    void RefreshStatus()
    {
        bool installed = HookInstaller.Installed();
        MonitorLabel.Text = L.T(paused ? "MonitorPaused" : installed ? "MonitorActive" : "IntegrationPending");
        var brush = (Brush)FindResource(paused || !installed ? "Muted" : "Success");
        MonitorLabel.Foreground = brush; MonitorDot.Fill = brush;
        PauseLabel.Text = L.T(paused ? "Resume" : "Pause"); PauseGlyph.Text = paused ? "\uE768" : "\uE769";
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, L.T(paused ? "ResumeAccessible" : "PauseAccessible"));
        FooterLabel.Text = L.T(installed ? "FooterActive" : "FooterInactive");
        IntegrationButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }
    SessionState? Selected() => SessionsList.SelectedItem is SessionRow row ? row.Session :
        lastAlert != null && state.Sessions.ContainsKey(lastAlert.Id) ? lastAlert :
        state.Sessions.Values.OrderByDescending(s => s.Pending && !s.Acknowledged).ThenByDescending(s => s.Last).FirstOrDefault();
    void UpdateAcknowledgement() => AcknowledgeButton.Visibility = Selected() is { Pending: true, Acknowledged: false } ? Visibility.Visible : Visibility.Hidden;
    void RenderSessions()
    {
        string? selected = (SessionsList.SelectedItem as SessionRow)?.Session.Id;
        rows.Clear();
        foreach (var s in state.Sessions.Values.OrderByDescending(s => s.Pending && !s.Acknowledged).ThenByDescending(s => s.Last)) rows.Add(new(s));
        SessionsList.SelectedItem = rows.FirstOrDefault(r => r.Session.Id == selected);
        SessionCount.Text = rows.Count switch { 0 => L.T("CountNone"), 1 => L.T("CountOne"), _ => L.F("CountMany", rows.Count) };
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAcknowledgement();
        UpdateTrayText();
    }
    internal void Receive(Signal signal, bool sound = true)
    {
        bool alert = state.Apply(signal);
        if (alert && state.Sessions.TryGetValue(signal.Session, out var session)) ShowAlert(session, sound);
        RenderSessions();
        if (card != null && card.SessionId != "demo" &&
            (!state.Sessions.TryGetValue(card.SessionId, out var active) || !active.Pending || active.Acknowledged)) CloseCard();
    }
    void Tick()
    {
        try
        {
            Directory.CreateDirectory(Storage.Inbox);
            foreach (string request in new[] { "exit.request", "show.request" })
            {
                string path = Path.Combine(Storage.Root, request);
                if (!File.Exists(path)) continue;
                bool fresh = DateTimeOffset.TryParse(File.ReadAllText(path), out var time) && DateTimeOffset.UtcNow - time < TimeSpan.FromSeconds(20);
                File.Delete(path); if (!fresh) continue;
                if (request == "exit.request") { Quit(); return; }
                Reveal();
            }
            foreach (string file in Directory.EnumerateFiles(Storage.Inbox, "*.json").Order().Take(200))
            {
                try
                {
                    var signal = JsonSerializer.Deserialize<Signal>(File.ReadAllText(file));
                    if (signal != null && signal.At >= DateTimeOffset.UtcNow.AddMinutes(-10) && signal.At <= DateTimeOffset.UtcNow.AddSeconds(30))
                        Receive(signal, signal.At >= DateTimeOffset.UtcNow.AddSeconds(-20));
                    File.Delete(file);
                }
                catch (JsonException) { File.Delete(file); }
                catch (IOException) { }
            }
            if (DateTimeOffset.UtcNow - lastClean > TimeSpan.FromMinutes(5))
            {
                lastClean = DateTimeOffset.UtcNow;
                foreach (var s in state.Sessions.Values.Where(s => s.Last < DateTimeOffset.UtcNow.AddHours(-8)).ToArray()) state.Sessions.Remove(s.Id);
                RenderSessions();
            }
        }
        catch (UnauthorizedAccessException) { FooterLabel.Text = L.T("EventAccessError"); }
        catch (IOException) { FooterLabel.Text = L.T("EventReadError"); }
        if (!paused && DateTimeOffset.UtcNow >= snoozedUntil && prefs.ReminderSeconds > 0)
        {
            var pending = state.Sessions.Values.Where(s => s.Pending && !s.Acknowledged && s.Status != "Resposta concluída" &&
                DateTimeOffset.UtcNow - s.LastAlert > TimeSpan.FromSeconds(prefs.ReminderSeconds)).OrderBy(s => s.LastAlert).FirstOrDefault();
            if (pending != null) ShowAlert(pending, true);
        }
    }
    void ShowAlert(SessionState session, bool sound)
    {
        if (paused || DateTimeOffset.UtcNow < snoozedUntil) return;
        session.LastAlert = DateTimeOffset.UtcNow;
        if (session.Id != "demo") { lastAlert = session; tray.Text = L.T("TrayPending"); }
        if (sound && prefs.Sound) SystemSounds.Exclamation.Play();
        if (!prefs.Popup) return;
        CloseCard();
        var shown = new AlertCard(session, () => OpenCode(session), () => Acknowledge(session), Snooze);
        card = shown; shown.Closed += (_, _) => { if (ReferenceEquals(card, shown)) card = null; }; shown.Show();
    }
    void CloseCard() { var previous = card; card = null; previous?.Close(); }
    void Acknowledge(SessionState? session)
    {
        if (session != null) session.Acknowledged = true;
        if (card?.SessionId == session?.Id) CloseCard(); RenderSessions();
    }
    void Snooze() { snoozedUntil = DateTimeOffset.UtcNow.AddMinutes(5); CloseCard(); FooterLabel.Text = L.T("Snoozed"); }
    internal void TestAlert()
    {
        if (paused || DateTimeOffset.UtcNow < snoozedUntil) { FooterLabel.Text = L.T(paused ? "TestWhilePaused" : "Snoozed"); return; }
        if (!prefs.Popup) { FooterLabel.Text = L.T("TestPopupDisabled"); return; }
        ShowAlert(new("demo") { Folder = L.T("ExampleProject"), Status = "Autorização necessária", Pending = true, Last = DateTimeOffset.UtcNow }, !diagnostics);
    }
    void OpenCode(SessionState? session)
    {
        try
        {
            var windows = Process.GetProcessesByName("Code").Where(p => p.MainWindowHandle != IntPtr.Zero).ToArray();
            var target = windows.FirstOrDefault(p => session != null && p.MainWindowTitle.Contains(session.Project, StringComparison.OrdinalIgnoreCase)) ?? windows.FirstOrDefault();
            if (target != null) { ShowWindow(target.MainWindowHandle, 9); SetForegroundWindow(target.MainWindowHandle); }
            else if (session != null && Path.IsPathFullyQualified(session.Folder) && Directory.Exists(session.Folder))
            {
                string uri = "vscode://file/" + string.Join("/", session.Folder.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else MessageBox.Show(this, L.T("CodeNotRunning"), L.T("AppTitle"));
        }
        catch { MessageBox.Show(this, L.T("CodeOpenError"), L.T("AppTitle")); }
    }
    void Sound_Changed(object sender, RoutedEventArgs e) { if (ready) { prefs.Sound = SoundToggle.IsChecked == true; SavePrefs(); } }
    void Popup_Changed(object sender, RoutedEventArgs e) { if (ready) { prefs.Popup = PopupToggle.IsChecked == true; SavePrefs(); if (!prefs.Popup) CloseCard(); } }
    void Reminder_Changed(object sender, SelectionChangedEventArgs e) { if (ready && ReminderSelect.SelectedIndex >= 0) { prefs.ReminderSeconds = new[] { 0, 30, 60, 120 }[ReminderSelect.SelectedIndex]; SavePrefs(); } }
    void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || LanguageSelect.SelectedValue is not string code) return;
        prefs.Language = L.Normalize(code); ApplyLanguage(); SavePrefs();
    }
    void Pause_Click(object sender, RoutedEventArgs e) { paused = !paused; if (paused) CloseCard(); RefreshStatus(); }
    void Test_Click(object sender, RoutedEventArgs e) => TestAlert();
    void OpenCode_Click(object sender, RoutedEventArgs e) => OpenCode(Selected());
    void Acknowledge_Click(object sender, RoutedEventArgs e) => Acknowledge(Selected());
    void Integration_Click(object sender, RoutedEventArgs e) => ChangeHooks(false);
    void Session_DoubleClick(object sender, MouseButtonEventArgs e) { if (SessionsList.SelectedItem is SessionRow row) OpenCode(row.Session); }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int command);
}

internal static class UiTheme
{
    internal static void ApplyAccessibility(ResourceDictionary resources)
    {
        if (!SystemParameters.HighContrast) return;
        foreach (string key in new[] { "Surface", "Ground", "Tint" }) resources[key] = SystemColors.WindowBrush;
        foreach (string key in new[] { "Ink", "Muted", "Line", "Success", "Danger" }) resources[key] = SystemColors.WindowTextBrush;
        foreach (string key in new[] { "Accent", "AccentHover", "AccentPressed" }) resources[key] = SystemColors.HighlightBrush;
        resources["OnAccent"] = SystemColors.HighlightTextBrush;
    }
}
