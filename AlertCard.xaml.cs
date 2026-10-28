using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;

namespace ClaudeAlerta;

public partial class AlertCard : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Stopwatch elapsed = new();
    readonly Action open, acknowledge;
    readonly SessionState session;
    internal string SessionId { get; }
    internal AlertCard(SessionState session, Action open, Action acknowledge, Action snooze)
    {
        InitializeComponent(); UiTheme.ApplyAccessibility(Resources);
        this.session = session; SessionId = session.Id; this.open = open; this.acknowledge = acknowledge;
        RefreshLanguage();
        var context = new System.Windows.Controls.ContextMenu();
        var silence = new System.Windows.Controls.MenuItem();
        silence.SetResourceReference(System.Windows.Controls.MenuItem.HeaderProperty, "Loc.Snooze");
        silence.Click += (_, _) => snooze(); context.Items.Add(silence); ContextMenu = context;
        Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            var source = HwndSource.FromHwnd(handle);
            var corner = source.CompositionTarget.TransformFromDevice.Transform(new Point(screen.Right, screen.Bottom));
            Left = corner.X - ActualWidth - 10; Top = corner.Y - ActualHeight - 10;
        };
        ContentRendered += (_, _) => { elapsed.Restart(); timer.Start(); };
        timer.Tick += (_, _) =>
        {
            double remaining = 3 - elapsed.Elapsed.TotalSeconds;
            if (remaining <= 0) { timer.Stop(); Close(); return; }
            Countdown.Text = L.F("Seconds", Math.Ceiling(remaining));
            TimeScale.ScaleX = SystemParameters.ClientAreaAnimation ? remaining / 3 : 1;
        };
        Closed += (_, _) => { timer.Stop(); elapsed.Stop(); };
    }
    internal void RefreshLanguage()
    {
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Current);
        AlertTitle.Text = L.Status(session.Status); AlertTitle.ToolTip = AlertTitle.Text;
        string project = SessionId == "demo" ? L.T("ExampleProject") : L.Project(session);
        ProjectLabel.Text = project + " · Claude Code"; ProjectLabel.ToolTip = project;
        Countdown.Text = L.F("Seconds", Math.Max(1, Math.Ceiling(3 - elapsed.Elapsed.TotalSeconds)));
    }
    void Open_Click(object sender, RoutedEventArgs e) => open();
    void Acknowledge_Click(object sender, RoutedEventArgs e) => acknowledge();
}
