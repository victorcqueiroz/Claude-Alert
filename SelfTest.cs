using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAlerta;

internal static class SelfTest
{
    internal static int Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_ALERT_DATA") is null || Environment.GetEnvironmentVariable("CLAUDE_ALERT_SETTINGS") is null) return 1;
        var checks = new List<string>();
        try
        {
            void Check(bool condition, string name)
            { if (!condition) throw new Exception(name); checks.Add(name); }
            Signal Event(string name, string tool = "", string id = "", string notification = "", string session = "one") =>
                new(session, @"C:\projects\example", name, tool, id, notification, DateTimeOffset.UtcNow);
            var state = new MonitorState();
            Check(!state.Apply(Event("UserPromptSubmit")), "Prompt inicia trabalho sem alerta");
            Check(state.Apply(Event("PermissionRequest", "Bash")), "Permissão gera alerta");
            Check(!state.Apply(Event("Notification", notification: "permission_prompt")), "Notificação não duplica permissão");
            state.Apply(Event("PostToolUse", "Read", "other"));
            Check(state.Sessions["one"].Pending, "Ferramenta paralela não remove autorização pendente");
            state.Apply(Event("PostToolUse", "Bash", "bash-id"));
            Check(!state.Sessions["one"].Pending, "Resultado da ferramenta limpa permissão");
            Check(state.Apply(Event("PreToolUse", "AskUserQuestion", "question-id")), "Pergunta gera alerta");
            state.Apply(Event("PostToolUse", "Read", "other"));
            Check(state.Sessions["one"].Pending, "Outro resultado não remove pergunta");
            state.Apply(Event("PostToolUse", "AskUserQuestion", "question-id"));
            Check(!state.Sessions["one"].Pending, "Resposta à pergunta limpa aviso");
            Check(state.Apply(Event("Stop")), "Resposta concluída gera aviso");
            Check(!state.Apply(Event("Notification", notification: "idle_prompt")) && !state.Sessions["one"].Pending,
                "Idle não duplica conclusão e conclusão não é pendência");
            state.Apply(Event("UserPromptSubmit"));
            Check(!state.Sessions["one"].Pending, "Novo prompt remove aviso concluído");
            Check(state.Apply(Event("PreToolUse", "ExitPlanMode", "plan-id")), "Plano gera alerta");
            state.Apply(Event("UserPromptSubmit", session: "two"));
            Check(state.Sessions["one"].Pending && !state.Sessions["two"].Pending, "Sessões são independentes");
            state.Apply(Event("SessionEnd"));
            Check(!state.Sessions.ContainsKey("one"), "Encerramento remove sessão");
            Check(!state.Apply(Event("Notification", notification: "auth_success", session: "two")), "Notificação irrelevante não alerta");
            var genericPermission = new MonitorState();
            genericPermission.Apply(Event("Notification", notification: "permission_prompt")); genericPermission.Apply(Event("UserPromptSubmit"));
            Check(genericPermission.Pending.Any(), "Novo prompt não confirma permissão sem metadados");
            genericPermission.Apply(Event("PermissionRequest", "Bash"));
            Check(genericPermission.Pending.Count() == 1 && genericPermission.Pending.Single().Request.Tool == "Bash", "Metadados substituem aviso genérico sem duplicação");
            var safe = Signal.Parse("{\"session_id\":\"safe\",\"hook_event_name\":\"PermissionRequest\",\"tool_input\":{\"command\":\"SECRET-123\"},\"message\":\"SECRET-456\",\"transcript_path\":\"SECRET-789\"}");
            Check(!JsonSerializer.Serialize(safe).Contains("SECRET"), "Dados sensíveis descartados");

            var parallel = new MonitorState();
            parallel.Apply(Event("PreToolUse", "Bash", "a")); parallel.Apply(Event("PreToolUse", "Bash", "b"));
            parallel.Apply(Event("PermissionRequest", "Bash")); parallel.Apply(Event("PermissionRequest", "Bash"));
            Check(parallel.Pending.Count() == 2, "Permissões paralelas não se substituem");
            parallel.Apply(Event("PostToolUse", "Bash", "a"));
            Check(parallel.Pending.Count() == 2, "Resultado ambíguo preserva as duas pendências");
            parallel.Apply(Event("UserPromptSubmit"));
            Check(parallel.Pending.Count() == 2, "Novo prompt não apaga permissões paralelas");
            parallel.Apply(Event("Stop"));
            Check(parallel.Pending.Count() == 2, "Conclusão não apaga permissões sem confirmação");
            var exact = new MonitorState();
            exact.Apply(Event("PreToolUse", "AskUserQuestion", "q1")); exact.Apply(Event("PreToolUse", "AskUserQuestion", "q2"));
            Check(exact.Pending.Count() == 2, "Duas perguntas na mesma sessão são independentes");
            exact.Apply(Event("PostToolUse", "AskUserQuestion", "q1"));
            Check(exact.Pending.Single().Request.ToolId == "q2", "Resposta por ID resolve apenas a pergunta correspondente");
            var duplicate = Event("PermissionRequest", "Write", session: "dup");
            exact.Apply(duplicate); exact.Apply(duplicate);
            Check(exact.Sessions["dup"].Requests.Count == 1, "Releitura de evento idêntico é idempotente");
            var stale = duplicate with { Event = "SessionEnd", At = duplicate.At.AddMinutes(-1) };
            exact.Apply(stale); Check(exact.Sessions.ContainsKey("dup"), "Evento atrasado não encerra sessão mais recente");
            var request = exact.Pending.First().Request; var now = DateTimeOffset.UtcNow;
            exact.Defer(request, 15, now);
            Check(exact.Pending.Count() == 2 && request.NextReminder == now.AddMinutes(15), "Adiamento conserva solicitação e agenda retorno");
            exact.Save(now); var restored = MonitorState.Load(now);
            Check(restored.Pending.Count() == 2 && restored.Sessions.Values.All(s => s.Restored), "Pendências sobrevivem ao reinício com estado restaurado");
            Check(restored.Pending.Any(p => p.Request.DeferredUntil == request.DeferredUntil), "Adiamento é restaurado do disco");
            restored.Apply(duplicate); Check(restored.Pending.Count() == 2, "Deduplicação sobrevive ao reinício");
            Check(restored.History.Any(h => h.Status == "RequestAnswered"), "Histórico preserva respostas confirmadas");
            restored.Prune(now.AddHours(25));
            Check(!restored.Pending.Any() && restored.History.Count == 0, "Retenção de 24 horas elimina registros antigos");
            var activeOld = new MonitorState(); activeOld.Apply(Event("PermissionRequest", "Write") with { At = now.AddHours(-25) }); activeOld.Apply(Event("PreToolUse", "Read") with { At = now }); activeOld.Prune(now);
            Check(!activeOld.Pending.Any() && activeOld.Sessions.Count == 1, "Solicitações também expiram em sessão com eventos recentes");
            File.WriteAllText(restored.FilePath, "{invalid");
            var recovered = MonitorState.Load(now); Check(recovered.LoadFailed, "Arquivo inválido produz estado de recuperação");
            recovered.Save(now);
            Check(Directory.GetFiles(Storage.Root, "activity.json.invalid-*.bak").Length == 1, "Snapshot inválido é preservado antes da gravação");
            File.WriteAllText(recovered.FilePath, "{\"Sessions\":[null]}");
            Check(MonitorState.Load(now).LoadFailed, "Estrutura JSON inválida é recuperada sem encerrar o monitor");
            Check(!recovered.Apply(duplicate with { Tool = null! }), "Evento com metadados nulos é ignorado");

            var policy = new Preferences { Sound = false };
            var sample = new SessionState("policy") { Folder = @"C:\projects\example" };
            var reminder = new PendingRequest { Created = now };
            Check(!NotificationPolicy.Due(policy, sample, reminder, now.AddSeconds(119)) && NotificationPolicy.Due(policy, sample, reminder, now.AddMinutes(2)), "Primeiro lembrete progressivo espera dois minutos");
            NotificationPolicy.Sent(policy, sample, reminder, now.AddMinutes(2));
            Check(reminder.NextReminder == now.AddMinutes(7), "Segundo lembrete usa intervalo de cinco minutos");
            NotificationPolicy.Sent(policy, sample, reminder, now.AddMinutes(7));
            Check(reminder.NextReminder == now.AddMinutes(22), "Terceiro lembrete usa intervalo de quinze minutos");
            NotificationPolicy.Sent(policy, sample, reminder, now.AddMinutes(22));
            Check(!NotificationPolicy.Due(policy, sample, reminder, now.AddHours(1)), "Limite impede lembretes infinitos");
            recovered.Defer(reminder, 5, now);
            Check(reminder.Reminders == 0 && !NotificationPolicy.Due(policy, sample, reminder, now.AddMinutes(4)) && NotificationPolicy.Due(policy, sample, reminder, now.AddMinutes(5)), "Adiamento reinicia limite e respeita prazo escolhido");
            policy.SetProfile("busy", 30, now);
            Check(NotificationPolicy.Allows(policy, sample.Folder, "action") && !NotificationPolicy.Allows(policy, sample.Folder, "done"), "Ocupado permite ação e agrupa conclusões");
            Check(!policy.ResumeIfDue(now.AddMinutes(29)) && policy.ResumeIfDue(now.AddMinutes(30)) && policy.Profile == "normal", "Retomada automática ocorre no prazo");
            policy.SetProfile("silent", 0, now); policy.Profiles["silent"].Action = true;
            Check(!NotificationPolicy.Allows(policy, sample.Folder, "action") && !policy.ResumeIfDue(now.AddDays(1)), "Silencioso bloqueia avisos e respeita retomada manual");
            policy.SetProfile("normal", 0, now);
            var project = policy.Project(sample.Folder); project.Muted = true;
            Check(!NotificationPolicy.Allows(policy, sample.Folder, "action") && NotificationPolicy.Allows(policy, @"C:\other", "action"), "Silêncio por projeto não afeta outro projeto");
            project.Muted = false; project.Done = false;
            Check(!NotificationPolicy.Allows(policy, sample.Folder, "done") && NotificationPolicy.Allows(policy, sample.Folder, "action"), "Regras por categoria são independentes");
            project.ReminderSeconds = 0; Check(!NotificationPolicy.Due(policy, sample, new PendingRequest { Created = now }, now.AddHours(1)), "Projeto pode desativar seus lembretes automáticos");
            Check(NotificationPolicy.Due(policy, sample, reminder, now.AddMinutes(5)), "Adiamento explícito permite um retorno mesmo sem lembretes automáticos");
            NotificationPolicy.Sent(policy, sample, reminder, now.AddMinutes(5));
            Check(!NotificationPolicy.Due(policy, sample, reminder, now.AddHours(1)), "Adiamento sem lembretes automáticos avisa somente uma vez");
            project.ReminderSeconds = -2; policy.ReminderSeconds = 30;
            Check(NotificationPolicy.Interval(policy, sample.Folder) == 30, "Projeto herda intervalo global");
            project.Alias = "Projeto favorito"; project.Pinned = true; policy.Save(); var loaded = Preferences.Load();
            Check(loaded.Project(sample.Folder).Alias == project.Alias && loaded.Project(sample.Folder).Pinned, "Apelido e projeto fixado são persistidos");
            Check(ReferenceEquals(loaded.Project(@"c:\PROJECTS\example\"), loaded.Project(sample.Folder)), "Pasta é normalizada sem diferenciar maiúsculas ou barra final");
            loaded.SetProfile("busy", 15, now); loaded.Save();
            Check(Preferences.Load().ResumeAt == now.AddMinutes(15), "Prazo do perfil sobrevive ao reinício");
            var absence = new Preferences(); absence.SetProfile("busy", 0, now); absence.SetProfile("silent", 0, now.AddMinutes(1)); absence.SetProfile("normal", 0, now.AddMinutes(2));
            Check(absence.ProfileSince == now, "Alternar entre perfis de ausência e voltar preserva o período do resumo");

            var original = JsonNode.Parse("{\"theme\":\"dark\",\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"existing-hook\"}]}],\"CustomEvent\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"custom-hook\"}]}]}}")!.AsObject();
            string before = original.ToJsonString();
            HookInstaller.Merge(original, @"C:\app\ClaudeAlerta.exe", false);
            string installed = original.ToJsonString();
            Check(installed.Contains("existing-hook") && installed.Contains("custom-hook") && original["theme"]!.ToString() == "dark",
                "Instalação preserva configurações e hooks existentes");
            HookInstaller.Merge(original, @"C:\app\ClaudeAlerta.exe", false);
            Check(original.ToJsonString() == installed, "Instalação repetida não duplica hooks");
            HookInstaller.Merge(original, @"C:\app\ClaudeAlerta.exe", true);
            Check(original.ToJsonString() == before, "Desinstalação preserva todos os hooks anteriores");
            safe.Enqueue();
            var queued = Directory.GetFiles(Storage.Inbox, "*.json");
            Check(queued.Length > 0 && JsonSerializer.Deserialize<Signal>(File.ReadAllText(queued[0]))?.Session == "safe", "Fila faz ida e volta com JSON válido");
            // Exercise the actual executable/stdin protocol while preventing diagnostic autostart.
            using (var hookMutex = new Mutex(false, Program.MutexName))
            using (var hook = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            { ArgumentList = { HookInstaller.Marker }, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!)
            {
                hook.StandardInput.Write("{\"session_id\":\"hook-roundtrip\",\"hook_event_name\":\"PermissionRequest\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"SECRET-PAYLOAD\"},\"transcript_path\":\"SECRET-TRANSCRIPT\"}");
                hook.StandardInput.Close();
                if (!hook.WaitForExit(5000)) { hook.Kill(); throw new Exception("Hook excedeu o prazo de teste"); }
                Check(hook.ExitCode == 0 && hook.StandardOutput.ReadToEnd().Length == 0, "Executável em modo hook encerra sem produzir decisão ou saída");
                var actual = Directory.GetFiles(Storage.Inbox, "*.json").Select(File.ReadAllText).Single(json => json.Contains("hook-roundtrip"));
                Check(!actual.Contains("SECRET") && JsonSerializer.Deserialize<Signal>(actual)?.Tool == "Bash", "Protocolo stdin grava somente metadados filtrados");
            }
            var legacy = JsonSerializer.Deserialize<Preferences>("{\"Sound\":false,\"Popup\":true,\"ReminderSeconds\":30}")!;
            Check(legacy.Language == L.DefaultLanguage && !legacy.Sound && legacy.Popup && legacy.ReminderSeconds == 30,
                "Preferências anteriores continuam em português sem perder suas opções");
            Check(L.Normalize(null) == L.DefaultLanguage && L.Normalize("unsupported") == L.DefaultLanguage && L.Normalize("FR") == "fr",
                "Idioma inválido usa português e códigos são normalizados");
            Check(L.Languages.Length == 6 && L.Languages.Select(l => l.Code).Distinct().Count() == 6,
                "Há seis idiomas distintos disponíveis");
            var expectedKeys = L.Catalog(L.DefaultLanguage).Keys.Order().ToArray();
            foreach (var language in L.Languages)
            {
                var catalog = L.Catalog(language.Code);
                Check(catalog.Keys.Order().SequenceEqual(expectedKeys) && catalog.Values.All(value => !string.IsNullOrWhiteSpace(value)),
                    "Catálogo completo e sem traduções vazias: " + language.Code);
                Check(catalog.All(entry => System.Text.CompositeFormat.Parse(entry.Value).MinimumArgumentCount ==
                    System.Text.CompositeFormat.Parse(L.Catalog(L.DefaultLanguage)[entry.Key]).MinimumArgumentCount),
                    "Parâmetros das mensagens são compatíveis: " + language.Code);
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { success = true, checks }, Storage.Json));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, Storage.Json));
            return 1;
        }
    }
}
