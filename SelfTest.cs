using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAlerta;

internal static class SelfTest
{
    internal static int Run(string output)
    {
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
            state.Sessions["one"].Acknowledged = true;
            Check(!state.Apply(Event("Notification", notification: "idle_prompt")) && state.Sessions["one"].Acknowledged,
                "Idle não repete resposta já vista");
            state.Apply(Event("UserPromptSubmit"));
            Check(!state.Sessions["one"].Pending, "Novo prompt remove aviso concluído");
            Check(state.Apply(Event("PreToolUse", "ExitPlanMode", "plan-id")), "Plano gera alerta");
            state.Apply(Event("UserPromptSubmit", session: "two"));
            Check(state.Sessions["one"].Pending && !state.Sessions["two"].Pending, "Sessões são independentes");
            state.Apply(Event("SessionEnd"));
            Check(!state.Sessions.ContainsKey("one"), "Encerramento remove sessão");
            Check(!state.Apply(Event("Notification", notification: "auth_success", session: "two")), "Notificação irrelevante não alerta");
            var safe = Signal.Parse("{\"session_id\":\"safe\",\"hook_event_name\":\"PermissionRequest\",\"tool_input\":{\"command\":\"SECRET-123\"},\"message\":\"SECRET-456\",\"transcript_path\":\"SECRET-789\"}");
            Check(!JsonSerializer.Serialize(safe).Contains("SECRET"), "Dados sensíveis descartados");

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
