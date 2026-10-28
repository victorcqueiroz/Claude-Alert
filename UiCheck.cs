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
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    internal static int Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_ALERT_DATA") is null || Environment.GetEnvironmentVariable("CLAUDE_ALERT_SETTINGS") is null) return 1;
        var checks = new List<string>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Dashboard? dashboard = null; int exit = 1; double dismissal = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void Capture(FrameworkElement visual, string name)
        {
            visual.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, name)); encoder.Save(stream);
        }
        async Task Idle() { if (dashboard != null) await dashboard.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Storage.Atomic(Storage.Settings, HookInstaller.Merge(new JsonObject(), Environment.ProcessPath!, false).ToJsonString());
            new Preferences { Sound = false, Popup = false }.Save();
            dashboard = new Dashboard(false, true) { ShowActivated = false };
            bool started = false;
            dashboard.ContentRendered += async (_, _) =>
            {
                if (started) return; started = true;
                try
                {
                    Signal Event(string session, string name, string tool = "", string id = "") => new(session,
                        session == "sample-one" ? @"C:\Exemplo\Meu projeto" : @"C:\Exemplo\Website pessoal", name, tool, id, "", DateTimeOffset.UtcNow);
                    Check(dashboard.ActivePage == "sessions" && dashboard.NavSessions.IsChecked == true, "Tela inicial é Sessões na navegação lateral");
                    Check(!Descendants<TabControl>(dashboard).Any(), "Janela não contém abas padrão ou navegação em duas fileiras");
                    Check(dashboard.ProfileAction.Visibility == Visibility.Visible && dashboard.ProfileRulesSection.Visibility == Visibility.Visible,
                        "Personalização do perfil já começa aberta sem expansor");
                    Check(new RadioButtonAutomationPeer(dashboard.NormalProfile).GetAutomationControlType() == AutomationControlType.RadioButton &&
                        new RadioButtonAutomationPeer(dashboard.NormalProfile).GetName() == L.T("ProfileNormal"), "Seletor de perfil tem semântica e nome acessíveis");
                    dashboard.Navigate("pending"); await Idle();
                    Check(dashboard.EmptyState.IsVisible && !dashboard.OpenCodeButton.IsEnabled, "Estado vazio sem ação inválida");
                    Check(dashboard.MonitorLabel.Text == L.T("MonitorActive"), "Configuração dos hooks reconhecida");
                    dashboard.Receive(Event("sample-one", "PermissionRequest", "Bash"), false);
                    dashboard.Receive(Event("sample-two", "UserPromptSubmit"), false); await Idle();
                    Check(dashboard.SessionsList.Items.Count == 1 && dashboard.DeferPanel.IsVisible, "Fila mostra somente solicitações pendentes");
                    var request = dashboard.Monitor.Pending.Single().Request;
                    dashboard.DeferSelect.SelectedIndex = 0; dashboard.DeferButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(dashboard.Monitor.Pending.Count() == 1 && request.DeferredUntil > DateTimeOffset.UtcNow.AddMinutes(4), "Adiar pela interface conserva a pendência");
                    Check(dashboard.DetailWait.Text.StartsWith(L.T("DeferredUntil").Split('{')[0]), "Detalhe mostra o prazo de adiamento");
                    Capture((FrameworkElement)dashboard.Content, "claro-pendencias.png");
                    dashboard.NavSessions.IsChecked = true; await Idle();
                    Check(dashboard.SessionOverview.Items.Count == 2 && !dashboard.DeferPanel.IsVisible, "Sessões reúne atividade e solicitações sem mostrar ações da fila");
                    var sessionBottom = dashboard.SessionOverviewSection.TranslatePoint(new Point(0, dashboard.SessionOverviewSection.ActualHeight), dashboard.OverviewContent);
                    var profileTop = dashboard.ProfileSection.TranslatePoint(new Point(), dashboard.OverviewContent);
                    Check(sessionBottom.Y < profileTop.Y, "Suas sessões fica acima dos perfis como no mockup aprovado");
                    Check(dashboard.NormalProfile.IsVisible && dashboard.BusyProfile.IsVisible && dashboard.SilentProfile.IsVisible, "Os três perfis ficam expostos sem abrir menus");
                    double normalCardWidth = dashboard.NormalProfile.ActualWidth;
                    Check(dashboard.OverviewPage.ScrollableHeight < 1, "Sessões e personalização cabem juntas na janela padrão com duas sessões");
                    Capture((FrameworkElement)dashboard.Content, "claro-desktop.png");
                    dashboard.Receive(Event("sample-one", "PostToolUse", "Bash"), false);
                    dashboard.NavHistory.IsChecked = true; await Idle();
                    Check(dashboard.SessionsList.Items.Cast<QueueRow>().Any(r => r.Status == L.T("RequestAnswered")), "Resposta confirmada entra em Recentes");
                    dashboard.Navigate("pending");
                    dashboard.Receive(Event("sample-one", "PreToolUse", "AskUserQuestion", "q1"), false);
                    dashboard.Receive(Event("sample-one", "PreToolUse", "AskUserQuestion", "q2"), false);
                    Check(dashboard.SessionsList.Items.Count == 2, "Perguntas paralelas ocupam linhas distintas");
                    dashboard.Receive(Event("sample-one", "PostToolUse", "AskUserQuestion", "q1"), false);
                    Check(dashboard.SessionsList.Items.Count == 1, "Resposta retira só a pergunta correspondente");

                    dashboard.Navigate("sessions"); dashboard.BusyProfile.IsChecked = true; await Idle();
                    Check(dashboard.Settings.Profile == "busy" && dashboard.Settings.ResumeAt > DateTimeOffset.UtcNow, "Perfil ocupado programa retomada");
                    Check(dashboard.ProfileRulesTitle.Text == L.F("ProfileRulesTitle", L.T("ProfileBusy")) && dashboard.ProfileAction.IsVisible &&
                        !dashboard.ProfileDone.IsChecked.GetValueOrDefault(), "Clicar no perfil mostra imediatamente suas próprias opções");
                    dashboard.ProfileDone.IsChecked = true; dashboard.NormalProfile.IsChecked = true; dashboard.BusyProfile.IsChecked = true;
                    Check(dashboard.ProfileDone.IsChecked == true && Preferences.Load().Profiles["busy"].Done, "Personalização persiste ao alternar perfis");
                    dashboard.ProfileDone.IsChecked = false;
                    dashboard.ResumeSelect.SelectedIndex = 1;
                    Check(Preferences.Load().ResumeMinutes == 15 && dashboard.ResumeControls.IsVisible, "Retomada é editável ao lado do perfil selecionado");
                    dashboard.ResumeSelect.SelectedIndex = 2;
                    await Idle();
                    Check(Math.Abs(dashboard.NormalProfile.ActualWidth - normalCardWidth) < 1 && dashboard.OverviewPage.ScrollableHeight < 1,
                        "Trocar para Ocupado mantém largura dos cartões e opções abertas na janela padrão");
                    Capture((FrameworkElement)dashboard.Content, "claro-ocupado.png");
                    dashboard.Receive(Event("sample-two", "Stop"), false);
                    Check(dashboard.VisibleCard == null && dashboard.Monitor.History.Any(h => h.Quiet && h.Status == "Resposta concluída"), "Conclusão no modo ocupado entra no resumo sem popup");
                    dashboard.PopupToggle.IsChecked = true; dashboard.SilentProfile.IsChecked = true;
                    dashboard.TestAlert(); Check(dashboard.IsPaused && dashboard.VisibleCard == null, "Silencioso bloqueia o teste de aviso");
                    Check(!dashboard.ProfileAction.IsEnabled && dashboard.ProfileAction.IsChecked == false &&
                        dashboard.ProfileRulesTitle.Text == L.F("ProfileRulesTitle", L.T("ProfileSilent")), "Silencioso mantém as opções expostas e desativadas");
                    dashboard.ReturnButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!dashboard.IsPaused && dashboard.NormalProfile.IsChecked == true && dashboard.SummaryLabel.Text.Contains("1"), "Voltei retoma normal e mostra o resumo");
                    dashboard.BusyProfile.IsChecked = true;
                    dashboard.Maintenance(DateTimeOffset.UtcNow.AddMinutes(31));
                    Check(dashboard.Settings.Profile == "normal", "Manutenção retoma automaticamente o perfil no prazo");

                    dashboard.NavProjects.IsChecked = true; await Idle();
                    dashboard.ProjectsList.SelectedItem = dashboard.ProjectsList.Items.Cast<ProjectRow>().First(r => r.Rules.Folder.EndsWith("Meu projeto"));
                    dashboard.AliasInput.Text = "Projeto favorito"; dashboard.AliasSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(dashboard.ProjectTitle.Text == "Projeto favorito" && Preferences.Load().Projects.Values.Any(p => p.Alias == "Projeto favorito"), "Apelido é aplicado e persistido");
                    dashboard.ProjectPin.IsChecked = true;
                    Check(((ProjectRow)dashboard.ProjectsList.Items[0]).Rules.Pinned, "Projeto fixado vai ao início da lista");
                    dashboard.ProjectDone.IsChecked = false;
                    Check(dashboard.Settings.Project(@"C:\Exemplo\Website pessoal").Done, "Regra individual não muda outro projeto");
                    dashboard.ProjectMute.IsChecked = true;
                    dashboard.Receive(Event("sample-one", "PermissionRequest", "Write"), false);
                    Check(dashboard.VisibleCard == null && dashboard.Monitor.Pending.Count() == 2, "Projeto silenciado continua recebendo pendências");
                    dashboard.ProjectMute.IsChecked = false; dashboard.CheckHealth();
                    Check(dashboard.HealthLabel.Text == L.T("HealthOk") && dashboard.Monitor.LastReceived != default, "Diagnóstico confere arquivos e mostra evento recebido");
                    Capture((FrameworkElement)dashboard.Content, "claro-projetos.png");

                    dashboard.Navigate("pending"); dashboard.Width = dashboard.MinWidth; dashboard.Height = dashboard.MinHeight;
                    foreach (var language in L.Languages)
                    {
                        dashboard.LanguageSelect.SelectedValue = language.Code; await Idle();
                        Check(L.Current == language.Code && dashboard.Headline.Text == L.F("PendingHeadline", 2), "Contagem traduzida sem reiniciar: " + language.Code);
                        Check(new ComboBoxAutomationPeer(dashboard.LanguageSelect).GetName() == L.T("Language") && dashboard.TrayLabels[0] == L.T("TrayOpen"), "Seletor e bandeja traduzidos: " + language.Code);
                        Check(dashboard.Monitor.Pending.Count() == 2 && Preferences.Load().Language == language.Code, "Idioma preserva pendências e fica salvo: " + language.Code);
                        foreach (var control in new FrameworkElement[] { dashboard.LanguageSelect, dashboard.NavPreferences, dashboard.NavSessions, dashboard.QueuePage, dashboard.OpenCodeButton, dashboard.DeferButton })
                        {
                            var root = (FrameworkElement)dashboard.Content; var origin = control.TranslatePoint(new Point(), root);
                            Check(control.ActualWidth > 0 && control.ActualHeight > 0 && origin.X >= 0 && origin.Y >= 0 &&
                                origin.X + control.ActualWidth <= root.ActualWidth + 1 && origin.Y + control.ActualHeight <= root.ActualHeight + 1,
                                "Controle cabe na janela mínima: " + language.Code + "/" + control.Name);
                        }
                        foreach (string page in new[] { "pending", "sessions", "history", "projects", "prefs" })
                        {
                            dashboard.Navigate(page); await Idle(); Capture((FrameworkElement)dashboard.Content, "idioma-" + language.Code + "-" + page + ".png");
                        }
                        dashboard.Navigate("sessions"); dashboard.ProfileAction.BringIntoView(); await Idle();
                        Check(dashboard.ProfileRulesTitle.Text == L.F("ProfileRulesTitle", L.T("ProfileNormal")) && dashboard.ProfileAction.IsVisible &&
                            new RadioButtonAutomationPeer(dashboard.BusyProfile).GetName() == L.T("ProfileBusy"), "Perfis e personalização traduzidos e acessíveis: " + language.Code);
                        var ruleOrigin = dashboard.ProfileAction.TranslatePoint(new Point(), dashboard.OverviewPage);
                        Check(ruleOrigin.Y >= -1 && ruleOrigin.Y + dashboard.ProfileAction.ActualHeight <= dashboard.OverviewPage.ViewportHeight + 1,
                            "Regras podem ser alcançadas por rolagem na janela mínima: " + language.Code);
                        Capture((FrameworkElement)dashboard.Content, "idioma-" + language.Code + "-regras.png");
                        dashboard.Navigate("prefs"); await Idle();
                        Check(new CheckBoxAutomationPeer(dashboard.SoundToggle).GetName() == L.T("Sound"), "Preferências acessíveis: " + language.Code);
                        dashboard.ReminderSelect.IsDropDownOpen = true; await Idle();
                        var popup = (System.Windows.Controls.Primitives.Popup)dashboard.ReminderSelect.Template.FindName("PART_Popup", dashboard.ReminderSelect);
                        Check(popup.IsOpen, "Menu de lembretes abre: " + language.Code);
                        dashboard.ReminderSelect.IsDropDownOpen = false;
                        dashboard.Navigate("pending");
                    }
                    dashboard.LanguageSelect.SelectedValue = "pt-BR";
                    dashboard.TestAlert(); var card = dashboard.VisibleCard!;
                    var closed = new TaskCompletionSource(); var watch = Stopwatch.StartNew(); card.Closed += (_, _) => closed.TrySetResult();
                    await Task.Delay(250); Capture((FrameworkElement)card.Content, "claro-aviso.png");
                    Check(!card.ShowActivated && !card.ShowInTaskbar && card.Topmost, "Aviso permanece breve e sem solicitar foco");
                    await Task.Delay(400); dashboard.LanguageSelect.SelectedValue = "de";
                    Check(card.AlertTitle.Text == L.T("StatusPermission"), "Aviso aberto acompanha o idioma");
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); dismissal = watch.Elapsed.TotalMilliseconds;
                    Check(dismissal >= 2700 && dismissal < 4500 && dashboard.VisibleCard == null, "Aviso fecha em três segundos sem clique nem reinício do prazo");
                    dashboard.Receive(Event("sample-one", "PreToolUse", "ExitPlanMode", "plan"), false);
                    Check(dashboard.VisibleCard != null, "Evento de ação abre aviso no perfil normal");
                    dashboard.Receive(Event("sample-one", "PostToolUse", "ExitPlanMode", "plan"), false);
                    Check(dashboard.VisibleCard == null, "Confirmação correspondente fecha aviso antes do prazo");
                    dashboard.LanguageSelect.SelectedValue = "fr";
                    dashboard.Settings.SetProfile("busy", 15, DateTimeOffset.UtcNow.AddHours(-1)); dashboard.Settings.Save();
                    dashboard.Close(); Check(!dashboard.IsVisible, "Fechar recolhe na bandeja");
                    var reopened = new Dashboard(false, true) { ShowActivated = false };
                    try
                    {
                        reopened.Show(); await Idle();
                        Check(reopened.LanguageSelect.SelectedValue?.ToString() == "fr" && reopened.Monitor.Pending.Count() == 2, "Reabertura restaura idioma e pendências");
                        Check(reopened.Monitor.Sessions.Values.All(s => s.Restored) && reopened.Settings.Projects.Values.Any(p => p.Pinned), "Reabertura identifica estado restaurado e recupera projetos");
                        Check(reopened.Settings.Profile == "normal" && Preferences.Load().Profile == "normal", "Reabertura retoma e salva Normal quando o prazo venceu durante o fechamento");
                    }
                    finally { reopened.Quit(); }
                    exit = 0;
                    File.WriteAllText(output, JsonSerializer.Serialize(new { success = true, checks, dismissalMilliseconds = dismissal }, Storage.Json));
                }
                catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, Storage.Json)); }
                finally { dashboard.Quit(); app.Shutdown(); }
            };
            app.Run(dashboard);
        }
        catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, Storage.Json)); dashboard?.Quit(); app.Shutdown(); }
        return exit;
    }
}
