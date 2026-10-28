using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClaudeAlerta;

internal static class UiCheck
{
    internal static int Run(string output)
    {
        var checks = new List<string>();
        if (Environment.GetEnvironmentVariable("CLAUDE_ALERT_DATA") is null || Environment.GetEnvironmentVariable("CLAUDE_ALERT_SETTINGS") is null)
            return 1; // Diagnostics must use explicitly isolated storage.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 1;
        Dashboard? dashboard = null;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void Capture(FrameworkElement visual, string name)
        {
            visual.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, name)); encoder.Save(stream);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Storage.Atomic(Storage.Settings, HookInstaller.Merge(new JsonObject(), Environment.ProcessPath!, false).ToJsonString());
            new Preferences { Sound = false, Popup = false, ReminderSeconds = 60 }.Save();
            dashboard = new Dashboard(false, true) { ShowActivated = false };
            bool started = false;
            dashboard.ContentRendered += async (_, _) =>
            {
                if (started) return; started = true;
                try
                {
                    Check(dashboard.EmptyState.IsVisible, "Estado vazio orienta a abrir o Claude");
                    Check(dashboard.MonitorLabel.Text == "Monitoramento ativo", "Integração reconhecida no painel");
                    Signal Event(string session, string name, string tool = "") => new(session, session == "sample-one" ? @"C:\Exemplo\Meu projeto" : @"C:\Exemplo\Website pessoal", name, tool, "", "", DateTimeOffset.UtcNow);
                    dashboard.Receive(Event("sample-one", "PermissionRequest", "Bash"), false);
                    dashboard.Receive(Event("sample-two", "UserPromptSubmit"), false);
                    await dashboard.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(dashboard.SessionsList.Items.Count == 2 && !dashboard.EmptyState.IsVisible, "Sessões substituem o estado vazio");
                    Check(dashboard.AcknowledgeButton.IsVisible, "Solicitação pendente permite reconhecimento");
                    Capture((FrameworkElement)dashboard.Content, "claro-desktop.png");
                    dashboard.Width = dashboard.MinWidth; dashboard.Height = dashboard.MinHeight;
                    await dashboard.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture((FrameworkElement)dashboard.Content, "claro-minimo.png");
                    foreach (FrameworkElement control in new FrameworkElement[] { dashboard.OpenCodeButton, dashboard.SoundToggle, dashboard.PopupToggle, dashboard.ReminderSelect, dashboard.PauseButton, dashboard.TestButton })
                    {
                        var origin = control.TranslatePoint(new Point(0, 0), dashboard.MainContent);
                        var root = dashboard.MainContent;
                        Check(control.ActualWidth > 0 && control.ActualHeight > 0 && origin.X >= 0 && origin.Y >= 0 &&
                            origin.X + control.ActualWidth <= root.ActualWidth + 1 && origin.Y + control.ActualHeight <= root.ActualHeight + 1,
                            "Controle cabe na janela mínima: " + control.Name);
                    }
                    var peer = new CheckBoxAutomationPeer(dashboard.SoundToggle);
                    Check(peer.GetAutomationControlType() == AutomationControlType.CheckBox && peer.GetName() == "Som do alerta", "Preferência possui nome e semântica acessíveis");
                    dashboard.ReminderSelect.IsDropDownOpen = true;
                    await dashboard.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var popup = (System.Windows.Controls.Primitives.Popup)dashboard.ReminderSelect.Template.FindName("PART_Popup", dashboard.ReminderSelect);
                    Check(popup.IsOpen && popup.Child is FrameworkElement menu && menu.ActualWidth >= dashboard.ReminderSelect.ActualWidth - 1, "Menu de lembretes abre com a largura do campo");
                    dashboard.SoundToggle.IsChecked = true; dashboard.ReminderSelect.SelectedIndex = 3;
                    dashboard.ReminderSelect.IsDropDownOpen = false;
                    var saved = Preferences.Load();
                    Check(saved.Sound && saved.ReminderSeconds == 120, "Preferências editadas são persistidas");
                    dashboard.SoundToggle.IsChecked = false;
                    dashboard.TestAlert(); Check(dashboard.VisibleCard is null, "Aviso desativado não abre janela");
                    dashboard.PopupToggle.IsChecked = true;
                    dashboard.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    dashboard.TestAlert(); Check(dashboard.IsPaused && dashboard.VisibleCard is null, "Pausa impede novos avisos");
                    dashboard.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!dashboard.IsPaused && dashboard.PauseLabel.Text == "Pausar", "Retomar atualiza o controle e o estado");
                    dashboard.TestAlert();
                    var card = dashboard.VisibleCard!;
                    var closed = new TaskCompletionSource(); var watch = Stopwatch.StartNew();
                    card.Closed += (_, _) => closed.TrySetResult();
                    await Task.Delay(200);
                    Capture((FrameworkElement)card.Content, "claro-aviso.png");
                    Check(!card.ShowActivated && !card.ShowInTaskbar && card.Topmost, "Aviso não solicita foco nem ocupa a barra de tarefas");
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    watch.Stop();
                    Check(watch.Elapsed.TotalSeconds >= 2.7 && watch.Elapsed.TotalSeconds < 4.5, "Aviso fecha automaticamente em aproximadamente três segundos");
                    Check(dashboard.VisibleCard is null, "Fechamento libera a referência ao aviso");
                    dashboard.Receive(Event("sample-one", "UserPromptSubmit"), false);
                    dashboard.Receive(Event("sample-one", "PermissionRequest", "Bash"), false);
                    Check(dashboard.VisibleCard != null, "Solicitação real do monitor abre o novo aviso");
                    dashboard.Receive(Event("sample-one", "PostToolUse", "Bash"), false);
                    Check(dashboard.VisibleCard is null, "Resposta à solicitação fecha o aviso antes do prazo");
                    dashboard.Close(); Check(!dashboard.IsVisible, "Fechar o painel o recolhe na bandeja");
                    exit = 0;
                    File.WriteAllText(output, JsonSerializer.Serialize(new { success = true, checks, dismissalMilliseconds = watch.Elapsed.TotalMilliseconds }, Storage.Json));
                }
                catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, Storage.Json)); }
                finally { dashboard.Quit(); app.Shutdown(); }
            };
            app.Run(dashboard);
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, Storage.Json));
            dashboard?.Quit(); app.Shutdown();
        }
        return exit;
    }
}
