using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClaudeAlerta;

internal sealed class QueueRow(SessionState session, Preferences prefs, PendingRequest? request = null, Activity? activity = null)
{
    public SessionState Session { get; } = session;
    public PendingRequest? Request { get; } = request;
    public Activity? Activity { get; } = activity;
    public string Key => Request?.Id ?? Activity?.Id ?? Session.Id;
    public string Folder => Session.Folder;
    public string Project => prefs.ProjectName(Session);
    public string SessionLabel => L.F("SessionLabel", Session.Id[..Math.Min(12, Session.Id.Length)]);
    public string SessionDetails => SessionLabel + " · " + Time;
    public string Status => L.Status(Request?.Status ?? Activity?.Status ?? (Session.Pending ? Session.Requests[0].Status : Session.Status));
    public string AccessibilityLabel => Project + ", " + SessionLabel + ", " + Status;
    public string Time => Request?.DeferredUntil > DateTimeOffset.UtcNow ? L.F("DeferredUntil", Request.DeferredUntil.ToLocalTime().ToString("t", L.Culture)) :
        Session.Restored && Activity == null ? L.T("RestoredState") : (Activity?.At ?? Request?.Created ?? Session.Last).ToLocalTime().ToString("g", L.Culture);
}
internal sealed record ProjectRow(string Key, string Name, string Detail, ProjectRules Rules)
{ public string AccessibilityLabel => Name + ", " + Detail; }

public partial class Dashboard : Window
{
    readonly MonitorState state = MonitorState.Load(DateTimeOffset.UtcNow);
    readonly Preferences prefs = Preferences.Load();
    readonly Forms.NotifyIcon tray;
    readonly Forms.ContextMenuStrip trayMenu = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly bool diagnostics;
    bool quitting, ready, rendering;
    AlertCard? card;
    string? cardRequest;
    DateTimeOffset lastClean, lastReminder;
    string activePage = "sessions";
    internal string ActivePage => activePage;
    internal bool IsPaused => prefs.Profile == "silent";
    internal AlertCard? VisibleCard => card;
    internal MonitorState Monitor => state;
    internal Preferences Settings => prefs;
    internal string[] TrayLabels => trayMenu.Items.Cast<Forms.ToolStripItem>().Select(item => item.Text ?? "").ToArray();
    static readonly int[] intervals = [-1, 0, 30, 60, 120];
    static readonly int[] projectIntervals = [-2, -1, 0, 30, 60, 120];
    static readonly int[] resumeMinutes = [0, 15, 30, 60];
    static readonly string[] profiles = ["normal", "busy", "silent"];

    internal Dashboard(bool trayOnly, bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        prefs.Normalize(); bool resumed = prefs.ResumeIfDue(DateTimeOffset.UtcNow); L.Apply(prefs.Language);
        InitializeComponent(); UiTheme.ApplyAccessibility(Resources);
        if (!diagnostics)
        {
            Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24));
            Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 24));
        }
        if (trayOnly) ShowActivated = false;
        void AddMenu(string key, Action action) { var item = trayMenu.Items.Add(L.T(key), null, (_, _) => action()); item.Tag = key; }
        AddMenu("TrayOpen", Reveal); AddMenu("TestAlert", TestAlert); AddMenu("Snooze", Snooze);
        AddMenu("ReturnNormal", ReturnToNormal); AddMenu("IntegrationDeactivate", () => ChangeHooks(true)); AddMenu("Exit", Quit);
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information;
        tray = new Forms.NotifyIcon { Icon = icon, Text = L.T("AppTitle"), Visible = !diagnostics, ContextMenuStrip = trayMenu };
        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        Closing += OnClosing;
        Closed += (_, _) => { timer.Stop(); CloseCard(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); icon.Dispose(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        ContentRendered += (_, _) => { if (trayOnly) Hide(); };
        LanguageSelect.ItemsSource = L.Languages;
        ApplyLanguage(); ready = true; Navigate(activePage);
        if (resumed) SavePrefs();
        if (state.LoadFailed || prefs.LoadFailed) FeedbackLabel.Text = L.T("RestoreError");
        timer.Tick += (_, _) => Tick();
        if (!diagnostics) timer.Start();
    }
    void OnClosing(object? sender, CancelEventArgs e) { if (!quitting) { e.Cancel = true; Hide(); } }
    internal void Quit() { Persist(); quitting = true; Close(); if (!diagnostics) Application.Current.Shutdown(); }
    void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    void SavePrefs() { try { prefs.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FeedbackLabel.Text = L.T("PrefsSaveError"); } }
    bool Persist() { try { state.Save(DateTimeOffset.UtcNow); return true; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FeedbackLabel.Text = L.T("ActivitySaveError"); return false; } }
    void ApplyLanguage()
    {
        bool before = ready; ready = false;
        try
        {
            L.Apply(prefs.Language); Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Current);
            LanguageSelect.SelectedValue = prefs.Language;
            SoundToggle.IsChecked = prefs.Sound; PopupToggle.IsChecked = prefs.Popup;
            ReminderSelect.SelectedIndex = Array.IndexOf(intervals, prefs.ReminderSeconds);
            foreach (Forms.ToolStripItem item in trayMenu.Items) if (item.Tag is string key) item.Text = L.T(key);
            HealthLabel.Text = ""; FeedbackLabel.Text = "";
            RenderAll(); card?.RefreshLanguage();
        }
        finally { ready = before; }
    }
    void ChangeHooks(bool remove)
    {
        try { HookInstaller.Install(remove); RefreshStatus(); FeedbackLabel.Text = L.T(remove ? "IntegrationRemoved" : "IntegrationAdded"); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { FeedbackLabel.Text = L.T(ex is JsonException ? "ConfigInvalid" : "ConfigWriteError"); }
    }
    void RefreshStatus()
    {
        bool installed = HookInstaller.Installed();
        MonitorLabel.Text = L.T(installed ? "MonitorActive" : "IntegrationPending");
        var monitorBrush = (System.Windows.Media.Brush)FindResource(installed ? "Success" : "Muted");
        MonitorLabel.Foreground = monitorBrush; MonitorDot.Fill = monitorBrush;
        FooterLabel.Text = L.T("RetentionFooter");
        IntegrationButton.Content = L.T(installed ? "IntegrationRefresh" : "IntegrationActivate");
        tray.Text = L.T(state.Pending.Any() ? "TrayPending" : "AppTitle");
        LastEventLabel.Text = state.LastReceived == default ? L.T("NoEventYet") : L.F("LastEvent", state.LastReceived.ToLocalTime().ToString("g", L.Culture));
    }
    void RenderAll() { RenderSessions(); RenderProfile(); RenderProjects(); RefreshStatus(); }
    internal void Navigate(string page)
    {
        if (page is not ("pending" or "sessions" or "history" or "projects" or "prefs")) return;
        activePage = page;
        QueuePage.Visibility = page is "pending" or "history" ? Visibility.Visible : Visibility.Collapsed;
        OverviewPage.Visibility = page == "sessions" ? Visibility.Visible : Visibility.Collapsed;
        ProjectsPage.Visibility = page == "projects" ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPage.Visibility = page == "prefs" ? Visibility.Visible : Visibility.Collapsed;
        GlobalProfileButton.Visibility = page == "sessions" ? Visibility.Collapsed : Visibility.Visible;
        bool before = rendering; rendering = true;
        try
        {
            NavPending.IsChecked = page == "pending"; NavSessions.IsChecked = page == "sessions";
            NavHistory.IsChecked = page == "history"; NavProjects.IsChecked = page == "projects"; NavPreferences.IsChecked = page == "prefs";
        }
        finally { rendering = before; }
        FeedbackLabel.Text = ""; RenderAll();
    }
    void UpdatePageHeading()
    {
        int count = state.Pending.Count();
        Headline.Text = activePage switch
        {
            "pending" => count == 0 ? L.T("NoPendingHeadline") : count == 1 ? L.T("OnePendingHeadline") : L.F("PendingHeadline", count),
            "history" => L.T("HistoryNav"), "projects" => L.T("ProjectsTab"), "prefs" => L.T("Preferences"), _ => L.T("WorkingTab")
        };
        PageSubtitle.Text = L.T(activePage switch { "pending" => "QueueSubtitle", "history" => "HistorySubtitle", "projects" => "ProjectsSubtitle", "prefs" => "PreferencesSubtitle", _ => "SessionsSubtitle" });
        QueueHelp.Text = L.T(activePage == "history" ? "HistoryHelp" : "PendingHelp");
    }
    internal void RenderSessions()
    {
        string? selected = (SessionsList.SelectedItem as QueueRow)?.Key;
        List<QueueRow> rows = activePage == "history"
            ? state.History.OrderByDescending(h => h.At).Select(h => new QueueRow(new(h.SessionId) { Folder = h.Folder, Last = h.At, Status = h.Status }, prefs, activity: h)).ToList()
            : state.Pending.OrderBy(p => p.Request.DeferredUntil > DateTimeOffset.UtcNow).ThenBy(p => p.Request.Created).Select(p => new QueueRow(p.Session, prefs, p.Request)).ToList();
        SessionsList.ItemsSource = rows; SessionsList.SelectedItem = rows.FirstOrDefault(r => r.Key == selected) ?? rows.FirstOrDefault();
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string? overviewSelection = (SessionOverview.SelectedItem as QueueRow)?.Session.Id;
        var overview = state.Sessions.Values.OrderByDescending(s => s.Pending).ThenByDescending(s => s.Last).Select(s => new QueueRow(s, prefs)).ToArray();
        SessionOverview.ItemsSource = overview;
        SessionOverview.SelectedItem = overview.FirstOrDefault(r => r.Session.Id == overviewSelection);
        OverviewEmpty.Visibility = overview.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewCount.Text = overview.Length switch { 0 => L.T("CountNone"), 1 => L.T("CountOne"), _ => L.F("CountMany", overview.Length) };
        PendingNavCount.Text = state.Pending.Count().ToString(L.Culture);
        UpdatePageHeading(); UpdateDetail();
    }
    QueueRow? SelectedRow => SessionsList.SelectedItem as QueueRow;
    void UpdateDetail()
    {
        var row = SelectedRow;
        DetailStatus.Text = row?.Status ?? L.T("EmptyQueue"); DetailProject.Text = row?.Project ?? "";
        DetailFolder.Text = row?.Folder ?? ""; DetailSession.Text = row?.SessionLabel ?? "";
        DetailTool.Text = row?.Request?.Tool is { Length: > 0 } tool ? L.F("ToolLabel", tool) : "";
        DetailWait.Text = row?.Time ?? ""; OpenCodeButton.IsEnabled = row != null;
        DeferPanel.Visibility = row?.Request != null ? Visibility.Visible : Visibility.Collapsed;
    }
    void RenderProfile()
    {
        rendering = true;
        try
        {
            NormalProfile.IsChecked = prefs.Profile == "normal";
            BusyProfile.IsChecked = prefs.Profile == "busy";
            SilentProfile.IsChecked = prefs.Profile == "silent";
            var rules = prefs.Profiles[prefs.Profile];
            bool silent = prefs.Profile == "silent";
            ProfileAction.IsChecked = !silent && rules.Action; ProfileDone.IsChecked = !silent && rules.Done;
            ProfileError.IsChecked = !silent && rules.Error; ProfileSound.IsChecked = !silent && rules.Sound;
            foreach (var toggle in new[] { ProfileAction, ProfileDone, ProfileError, ProfileSound }) toggle.IsEnabled = prefs.Profile != "silent";
            string profileName = L.T(prefs.Profile == "normal" ? "ProfileNormal" : prefs.Profile == "busy" ? "ProfileBusy" : "ProfileSilent");
            ProfileRulesTitle.Text = L.F("ProfileRulesTitle", profileName);
            ProfileRulesHelp.Text = L.T(silent ? "SilentRulesHelp" : "ProfileRulesHelp");
            ProfileStateLabel.Text = L.F("ProfileActiveLabel", profileName);
            GlobalProfileButton.Content = L.F("CurrentProfile", profileName);
            BusyDescription.Text = L.T(prefs.Profiles["busy"].Action && prefs.Profiles["busy"].Error && !prefs.Profiles["busy"].Done ? "ProfileBusyDescription" : "ProfileCustomDescription");
            System.Windows.Automation.AutomationProperties.SetHelpText(BusyProfile, BusyDescription.Text);
            ResumeSelect.SelectedIndex = Math.Max(0, Array.IndexOf(resumeMinutes, prefs.ResumeMinutes));
            ResumeControls.Visibility = prefs.Profile == "normal" ? Visibility.Collapsed : Visibility.Visible;
            ResumeLabel.Visibility = prefs.Profile == "normal" ? Visibility.Collapsed : Visibility.Visible;
            ResumeLabel.Text = prefs.ResumeAt == default ? L.T("ResumeManual") : L.F("ResumeAt", prefs.ResumeAt.ToLocalTime().ToString("t", L.Culture));
            int completed = state.History.Count(h => h.Quiet && h.At >= prefs.ProfileSince && h.Status == "Resposta concluída");
            int errors = state.History.Count(h => h.Quiet && h.At >= prefs.ProfileSince && h.Status == "Claude interrompido por erro");
            SummaryLabel.Text = completed == 0 && errors == 0 ? L.T("NoQuietEvents") : L.F("QuietSummary", completed, errors);
        }
        finally { rendering = false; }
    }
    ProjectRow? SelectedProject => ProjectsList.SelectedItem as ProjectRow;
    void RenderProjects()
    {
        string? key = SelectedProject?.Key;
        foreach (var s in state.Sessions.Values) prefs.Project(s.Folder);
        var items = prefs.Projects.Select(p => new ProjectRow(p.Key,
            (p.Value.Pinned ? "★ " : "") + (string.IsNullOrWhiteSpace(p.Value.Alias) ? L.Project(new("") { Folder = p.Value.Folder }) : p.Value.Alias),
            L.F("CountMany", state.Sessions.Values.Count(s => Preferences.ProjectKey(s.Folder) == p.Key)), p.Value))
            .OrderByDescending(p => p.Rules.Pinned).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        rendering = true;
        try { ProjectsList.ItemsSource = items; ProjectsList.SelectedItem = items.FirstOrDefault(p => p.Key == key) ?? items.FirstOrDefault(); UpdateProject(); }
        finally { rendering = false; }
    }
    void UpdateProject()
    {
        bool before = rendering; rendering = true;
        try
        {
            var row = SelectedProject; ProjectPanel.IsEnabled = row != null;
            ProjectTitle.Text = row?.Name ?? L.T("EmptyProjects"); ProjectFolder.Text = row?.Rules.Folder ?? "";
            if (!AliasInput.IsKeyboardFocusWithin) AliasInput.Text = row?.Rules.Alias ?? "";
            ProjectPin.IsChecked = row?.Rules.Pinned ?? false; ProjectMute.IsChecked = row?.Rules.Muted ?? false;
            ProjectAction.IsChecked = row?.Rules.Action ?? true; ProjectDone.IsChecked = row?.Rules.Done ?? true; ProjectError.IsChecked = row?.Rules.Error ?? true;
            ProjectReminder.SelectedIndex = Math.Max(0, Array.IndexOf(projectIntervals, row?.Rules.ReminderSeconds ?? -2));
            ProjectSessions.Text = row == null ? "" : string.Join(Environment.NewLine, state.Sessions.Values.Where(s => Preferences.ProjectKey(s.Folder) == row.Key)
                .Select(s => L.F("SessionLabel", s.Id[..Math.Min(12, s.Id.Length)]) + " · " + L.Status(s.Pending ? s.Requests[0].Status : s.Status)));
        }
        finally { rendering = before; }
    }
    internal bool Receive(Signal signal, bool sound = true)
    {
        bool alert = state.Apply(signal);
        if (alert && state.LastNotice is { } notice)
        {
            bool fresh = signal.At >= DateTimeOffset.UtcNow.AddSeconds(-20);
            if (!fresh || !ShowAlert(notice, sound)) state.MarkQuiet(notice);
        }
        bool saved = Persist(); RenderAll();
        if (cardRequest != null && !state.Pending.Any(p => p.Request.Id == cardRequest)) CloseCard();
        return saved;
    }
    internal void Maintenance(DateTimeOffset now)
    {
        if (prefs.ResumeIfDue(now)) { SavePrefs(); RenderAll(); FeedbackLabel.Text = L.T("ResumedSummary"); }
        if (now - lastClean > TimeSpan.FromMinutes(1)) { lastClean = now; Persist(); RenderAll(); }
        if (now - lastReminder < TimeSpan.FromSeconds(10)) return;
        var pending = state.Pending.Where(p => NotificationPolicy.Due(prefs, p.Session, p.Request, now)).OrderBy(p => p.Request.NextReminder == default ? p.Request.Created : p.Request.NextReminder).FirstOrDefault();
        if (pending.Request != null && ShowAlert(new(pending.Session, pending.Request.Status, "action", pending.Request), true))
        { NotificationPolicy.Sent(prefs, pending.Session, pending.Request, now); lastReminder = now; Persist(); }
    }
    void Tick()
    {
        try
        {
            Directory.CreateDirectory(Storage.Inbox);
            foreach (string request in new[] { "exit.request", "show.request" })
            {
                string path = Path.Combine(Storage.Root, request); if (!File.Exists(path)) continue;
                bool fresh = DateTimeOffset.TryParse(File.ReadAllText(path), out var time) && DateTimeOffset.UtcNow - time < TimeSpan.FromSeconds(20);
                File.Delete(path); if (!fresh) continue;
                if (request == "exit.request") { Quit(); return; } Reveal();
            }
            foreach (string file in Directory.EnumerateFiles(Storage.Inbox, "*.json").Order().Take(200))
            {
                try
                {
                    var signal = JsonSerializer.Deserialize<Signal>(File.ReadAllText(file));
                    if (signal != null && signal.At >= DateTimeOffset.UtcNow.AddMinutes(-10) && signal.At <= DateTimeOffset.UtcNow.AddSeconds(30) && !Receive(signal)) continue;
                    File.Delete(file);
                }
                catch (JsonException) { File.Delete(file); }
                catch (IOException) { }
            }
            Maintenance(DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FeedbackLabel.Text = L.T("EventReadError"); }
    }
    bool ShowAlert(Notice notice, bool sound)
    {
        if (!NotificationPolicy.Allows(prefs, notice.Session.Folder, notice.Category)) return false;
        if (sound && prefs.Sound && prefs.Profiles[prefs.Profile].Sound && !diagnostics) SystemSounds.Exclamation.Play();
        if (!prefs.Popup) return prefs.Sound && prefs.Profiles[prefs.Profile].Sound;
        CloseCard();
        var display = new SessionState(notice.Session.Id) { Folder = notice.Session.Folder, Status = notice.Status, Last = notice.Session.Last };
        var shown = new AlertCard(display, () => OpenCode(notice.Session), () => Defer(notice.Request, 15), Snooze, prefs.ProjectName(notice.Session));
        card = shown; cardRequest = notice.Request?.Id;
        shown.Closed += (_, _) => { if (ReferenceEquals(card, shown)) { card = null; cardRequest = null; } }; shown.Show(); return true;
    }
    void CloseCard() { var previous = card; card = null; cardRequest = null; previous?.Close(); }
    internal void Defer(PendingRequest? request, int minutes)
    {
        if (request == null) { CloseCard(); return; }
        state.Defer(request, minutes, DateTimeOffset.UtcNow); CloseCard(); Persist(); RenderSessions(); FeedbackLabel.Text = L.F("DeferredMinutes", minutes);
    }
    void Snooze() { prefs.SetProfile("silent", 5, DateTimeOffset.UtcNow); CloseCard(); SavePrefs(); RenderAll(); FeedbackLabel.Text = L.T("Snoozed"); }
    internal void ReturnToNormal() { prefs.Profile = "normal"; prefs.ResumeAt = default; SavePrefs(); Navigate("sessions"); FeedbackLabel.Text = L.T("ResumedSummary"); }
    internal void TestAlert()
    {
        if (!prefs.Popup) { FeedbackLabel.Text = L.T("TestPopupDisabled"); return; }
        var demo = new SessionState("demo") { Folder = L.T("ExampleProject"), Status = "Autorização necessária", Last = DateTimeOffset.UtcNow };
        if (!ShowAlert(new(demo, demo.Status, "action"), !diagnostics)) FeedbackLabel.Text = L.T("TestWhilePaused");
    }
    void OpenCode(SessionState? session)
    {
        try
        {
            var windows = Process.GetProcessesByName("Code").Where(p => p.MainWindowHandle != IntPtr.Zero).ToArray();
            bool hasFolder = session != null && Path.IsPathFullyQualified(session.Folder) && Directory.Exists(session.Folder);
            var target = windows.FirstOrDefault(p => session != null && p.MainWindowTitle.Contains(session.Project, StringComparison.OrdinalIgnoreCase)) ?? (hasFolder ? null : windows.FirstOrDefault());
            if (target != null) { ShowWindow(target.MainWindowHandle, 9); SetForegroundWindow(target.MainWindowHandle); }
            else if (session != null && Path.IsPathFullyQualified(session.Folder) && Directory.Exists(session.Folder))
            {
                string uri = "vscode://file/" + string.Join("/", session.Folder.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else FeedbackLabel.Text = L.T("CodeNotRunning");
        }
        catch { FeedbackLabel.Text = L.T("CodeOpenError"); }
    }
    void Sound_Changed(object sender, RoutedEventArgs e) { if (ready) { prefs.Sound = SoundToggle.IsChecked == true; SavePrefs(); } }
    void Popup_Changed(object sender, RoutedEventArgs e) { if (ready) { prefs.Popup = PopupToggle.IsChecked == true; SavePrefs(); if (!prefs.Popup) CloseCard(); } }
    void Reminder_Changed(object sender, SelectionChangedEventArgs e) { if (ready && ReminderSelect.SelectedIndex >= 0) { prefs.ReminderSeconds = intervals[ReminderSelect.SelectedIndex]; SavePrefs(); } }
    void Language_Changed(object sender, SelectionChangedEventArgs e) { if (ready && LanguageSelect.SelectedValue is string code) { prefs.Language = L.Normalize(code); ApplyLanguage(); SavePrefs(); } }
    void Test_Click(object sender, RoutedEventArgs e) => TestAlert();
    void OpenCode_Click(object sender, RoutedEventArgs e) => OpenCode(SelectedRow?.Session);
    void Defer_Click(object sender, RoutedEventArgs e) => Defer(SelectedRow?.Request, new[] { 5, 15, 30 }[Math.Max(0, DeferSelect.SelectedIndex)]);
    void Integration_Click(object sender, RoutedEventArgs e) => ChangeHooks(false);
    void Session_Selected(object sender, SelectionChangedEventArgs e) { if (ready) UpdateDetail(); }
    void Session_DoubleClick(object sender, MouseButtonEventArgs e) => OpenCode(SelectedRow?.Session);
    void Overview_DoubleClick(object sender, MouseButtonEventArgs e) => OpenCode((SessionOverview.SelectedItem as QueueRow)?.Session);
    void Navigation_Checked(object sender, RoutedEventArgs e) { if (ready && !rendering && sender is RadioButton { Tag: string page, IsChecked: true }) Navigate(page); }
    void Profiles_Click(object sender, RoutedEventArgs e)
    {
        Navigate("sessions");
        var target = prefs.Profile == "normal" ? NormalProfile : prefs.Profile == "busy" ? BusyProfile : SilentProfile;
        target.BringIntoView(); target.Focus();
    }
    void History_Click(object sender, RoutedEventArgs e) => Navigate("history");
    void Profile_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering || sender is not RadioButton { Tag: string profile, IsChecked: true } || !profiles.Contains(profile) || prefs.Profile == profile) return;
        prefs.SetProfile(profile, prefs.ResumeMinutes, DateTimeOffset.UtcNow); CloseCard(); SavePrefs(); RenderAll();
    }
    void ProfileRule_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering) return;
        var rules = prefs.Profiles[prefs.Profile]; rules.Action = ProfileAction.IsChecked == true; rules.Done = ProfileDone.IsChecked == true;
        rules.Error = ProfileError.IsChecked == true; rules.Sound = ProfileSound.IsChecked == true; CloseCard(); SavePrefs(); RenderProfile();
    }
    void Resume_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || rendering || ResumeSelect.SelectedIndex < 0) return;
        prefs.ResumeMinutes = resumeMinutes[ResumeSelect.SelectedIndex]; prefs.ResumeAt = prefs.Profile != "normal" && prefs.ResumeMinutes > 0 ? DateTimeOffset.UtcNow.AddMinutes(prefs.ResumeMinutes) : default;
        SavePrefs(); RenderProfile();
    }
    void Return_Click(object sender, RoutedEventArgs e) => ReturnToNormal();
    void Project_Selected(object sender, SelectionChangedEventArgs e) { if (!rendering) { AliasInput.Text = ""; UpdateProject(); } }
    void Alias_Click(object sender, RoutedEventArgs e) { if (SelectedProject is { } row) { row.Rules.Alias = AliasInput.Text.Trim(); SavePrefs(); RenderAll(); } }
    void ProjectRule_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering || SelectedProject is not { } row) return;
        row.Rules.Pinned = ProjectPin.IsChecked == true; row.Rules.Muted = ProjectMute.IsChecked == true;
        row.Rules.Action = ProjectAction.IsChecked == true; row.Rules.Done = ProjectDone.IsChecked == true; row.Rules.Error = ProjectError.IsChecked == true;
        CloseCard(); SavePrefs(); RenderProjects();
    }
    void ProjectReminder_Changed(object sender, SelectionChangedEventArgs e)
    { if (ready && !rendering && SelectedProject is { } row && ProjectReminder.SelectedIndex >= 0) { row.Rules.ReminderSeconds = projectIntervals[ProjectReminder.SelectedIndex]; SavePrefs(); } }
    internal void CheckHealth()
    {
        try
        {
            Directory.CreateDirectory(Storage.Inbox); string probe = Path.Combine(Storage.Inbox, Guid.NewGuid().ToString("N") + ".probe");
            try { File.WriteAllText(probe, "ok"); } finally { if (File.Exists(probe)) File.Delete(probe); }
            HealthLabel.Text = L.T(HookInstaller.Installed() && File.Exists(Environment.ProcessPath) ? "HealthOk" : "HealthConfigMissing");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { HealthLabel.Text = L.T("HealthQueueError"); }
        RefreshStatus();
    }
    void Health_Click(object sender, RoutedEventArgs e) => CheckHealth();
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
