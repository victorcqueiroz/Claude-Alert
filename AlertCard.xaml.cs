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
    internal string SessionId { get; }
    internal AlertCard(SessionState session, Action open, Action acknowledge, Action snooze)
    {
        InitializeComponent(); UiTheme.ApplyAccessibility(Resources);
        SessionId = session.Id; this.open = open; this.acknowledge = acknowledge;
        AlertTitle.Text = session.Status; AlertTitle.ToolTip = session.Status;
        ProjectLabel.Text = session.Project + " · Claude Code"; ProjectLabel.ToolTip = session.Project;
        var context = new System.Windows.Controls.ContextMenu();
        var silence = new System.Windows.Controls.MenuItem { Header = "Silenciar por 5 minutos" };
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
            Countdown.Text = Math.Ceiling(remaining) + " s";
            TimeScale.ScaleX = SystemParameters.ClientAreaAnimation ? remaining / 3 : 1;
        };
        Closed += (_, _) => { timer.Stop(); elapsed.Stop(); };
    }
    void Open_Click(object sender, RoutedEventArgs e) => open();
    void Acknowledge_Click(object sender, RoutedEventArgs e) => acknowledge();
}
