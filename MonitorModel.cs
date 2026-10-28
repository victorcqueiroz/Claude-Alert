using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeAlerta;

internal sealed class PendingRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "Resposta necessária";
    public string Tool { get; set; } = "";
    public string ToolId { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset DeferredUntil { get; set; }
    public DateTimeOffset NextReminder { get; set; }
    public int Reminders { get; set; }
}

internal sealed record ToolInvocation(string Tool, string Id, DateTimeOffset At);
internal sealed class SessionState
{
    public SessionState() { }
    public SessionState(string id) => Id = id;
    public string Id { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Status { get; set; } = "Conectado";
    public DateTimeOffset Last { get; set; }
    public bool Restored { get; set; }
    public List<PendingRequest> Requests { get; set; } = [];
    public List<ToolInvocation> Tools { get; set; } = [];
    [JsonIgnore] public bool Pending => Requests.Count > 0;
    [JsonIgnore] public string Project => string.IsNullOrEmpty(Folder) ? "Sessão Claude" : Path.GetFileName(Folder.TrimEnd('/', '\\'));
}

internal sealed record Activity(string Id, string SessionId, string Folder, string Status, DateTimeOffset At, bool Quiet = false);
internal sealed record Notice(SessionState Session, string Status, string Category, PendingRequest? Request = null);
internal sealed class MonitorSnapshot
{
    public int Version { get; set; } = 1;
    public List<SessionState> Sessions { get; set; } = [];
    public List<Activity> History { get; set; } = [];
    public List<string> Processed { get; set; } = [];
    public DateTimeOffset LastReceived { get; set; }
}

internal sealed class MonitorState
{
    internal readonly Dictionary<string, SessionState> Sessions = [];
    internal readonly List<Activity> History = [];
    readonly List<string> processed = [];
    internal DateTimeOffset LastReceived;
    internal Notice? LastNotice;
    internal bool LoadFailed;
    internal string FilePath => Path.Combine(Storage.Root, "activity.json");
    internal IEnumerable<(SessionState Session, PendingRequest Request)> Pending =>
        Sessions.Values.SelectMany(s => s.Requests.Select(r => (s, r)));
    static string Signature(Signal e) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e))));

    internal bool Apply(Signal e)
    {
        LastNotice = null;
        if (e.Folder == null || e.Tool == null || e.ToolId == null || e.Notification == null) return false;
        if (!HookInstaller.Events.Contains(e.Event) || string.IsNullOrWhiteSpace(e.Session)) return false;
        string key = Signature(e);
        if (processed.Contains(key)) return false;
        if (Sessions.TryGetValue(e.Session, out var known) && e.At < known.Last) return false;
        processed.Add(key); if (processed.Count > 512) processed.RemoveAt(0);
        LastReceived = e.At > LastReceived ? e.At : LastReceived;
        if (!Sessions.TryGetValue(e.Session, out var s)) Sessions[e.Session] = s = new(e.Session);
        s.Last = e.At; s.Restored = false;
        if (!string.IsNullOrWhiteSpace(e.Folder)) s.Folder = e.Folder;
        s.Tools.RemoveAll(t => e.At - t.At > TimeSpan.FromHours(8));
        if (e.Event == "SessionEnd")
        {
            foreach (var request in s.Requests) AddHistory(s, "SessionClosed", e.At);
            if (!s.Pending) AddHistory(s, "SessionClosed", e.At);
            Sessions.Remove(s.Id); return false;
        }
        if (e.Event == "UserPromptSubmit")
        {
            // A new prompt proves renewed input, but not resolution of parallel tool permissions.
            Resolve(s, r => r.Tool.Length == 0 && r.Status == "Resposta necessária", e.At);
            s.Status = "Trabalhando"; return false;
        }
        if (e.Event == "PreToolUse" && e.ToolId.Length > 0 && !s.Tools.Any(t => t.Id == e.ToolId))
        {
            s.Tools.Add(new(e.Tool, e.ToolId, e.At));
            if (s.Tools.Count > 128) s.Tools.RemoveAt(0);
        }
        if (e.Event is "PostToolUse" or "PostToolUseFailure")
        {
            // Exact IDs are authoritative. Name-only correlation is safe only for one candidate.
            var candidates = s.Requests.Where(r => r.Tool == e.Tool).ToArray();
            bool unique = candidates.Length == 1 && candidates[0].ToolId.Length == 0 &&
                s.Tools.Count(t => t.Tool == e.Tool) <= 1;
            Resolve(s, r => (r.ToolId.Length > 0 && e.ToolId.Length > 0 && r.ToolId == e.ToolId) ||
                (unique && r.Id == candidates[0].Id), e.At);
            s.Tools.RemoveAll(t => t.Id == e.ToolId && e.ToolId.Length > 0);
            s.Status = "Trabalhando"; return false;
        }
        string? status = e.Event switch
        {
            "PermissionRequest" => "Autorização necessária",
            "PreToolUse" when e.Tool == "AskUserQuestion" => "Claude fez uma pergunta",
            "PreToolUse" when e.Tool == "ExitPlanMode" => "Plano aguardando aprovação",
            "Stop" => "Resposta concluída",
            "StopFailure" => "Claude interrompido por erro",
            "Notification" => e.Notification switch
            {
                "permission_prompt" => "Autorização necessária",
                "idle_prompt" => "Resposta concluída",
                "elicitation_dialog" or "elicitation_url_dialog" or "agent_needs_input" => "Resposta necessária",
                _ => null
            },
            _ => null
        };
        if (status == null) { if (e.Event == "PreToolUse") s.Status = "Trabalhando"; return false; }
        if (status is "Resposta concluída" or "Claude interrompido por erro")
        {
            // Idle reminders do not duplicate Stop, and neither closes unresolved permissions.
            if (s.Status == status) return false;
            s.Status = status;
            AddHistory(s, status, e.At);
            LastNotice = new(s, status, status == "Resposta concluída" ? "done" : "error");
            return true;
        }
        string toolId = e.ToolId;
        // PermissionRequest lacks tool_use_id. Preserve ambiguity instead of guessing a parallel call.
        if (toolId.Length == 0 && e.Tool.Length > 0)
        {
            var possible = s.Tools.Where(t => t.Tool == e.Tool).ToArray();
            if (possible.Length == 1) toolId = possible[0].Id;
        }
        var existing = s.Requests.FirstOrDefault(r => r.Status == status &&
            (e.Event == "Notification" || (toolId.Length > 0 && r.ToolId == toolId)));
        if (existing != null) return false;
        // A richer event replaces the earlier generic notification without creating a second request.
        var generic = s.Requests.FirstOrDefault(r => r.Status == status && r.Tool.Length == 0);
        if (generic != null && e.Tool.Length > 0)
        { generic.Tool = e.Tool; generic.ToolId = toolId; return false; }
        var pending = new PendingRequest { Status = status, Tool = e.Tool, ToolId = toolId, Created = e.At };
        s.Requests.Add(pending); s.Status = status;
        LastNotice = new(s, status, "action", pending); return true;
    }
    void Resolve(SessionState s, Func<PendingRequest, bool> predicate, DateTimeOffset at)
    {
        foreach (var request in s.Requests.Where(predicate).ToArray())
        { s.Requests.Remove(request); AddHistory(s, "RequestAnswered", at); }
    }
    void AddHistory(SessionState s, string status, DateTimeOffset at) => History.Add(new(Guid.NewGuid().ToString("N"), s.Id, s.Folder, status, at));
    internal void MarkQuiet(Notice notice)
    {
        if (notice.Request != null) return;
        int index = History.FindLastIndex(h => h.SessionId == notice.Session.Id && h.Status == notice.Status);
        if (index >= 0) History[index] = History[index] with { Quiet = true };
    }
    internal void Defer(PendingRequest request, int minutes, DateTimeOffset now)
    {
        request.DeferredUntil = now.AddMinutes(minutes); request.NextReminder = request.DeferredUntil; request.Reminders = 0;
    }
    internal void Prune(DateTimeOffset now)
    {
        History.RemoveAll(h => h.At < now.AddHours(-24));
        if (History.Count > 500) History.RemoveRange(0, History.Count - 500);
        foreach (var s in Sessions.Values)
            if (s.Requests.RemoveAll(r => r.Created < now.AddHours(-24)) > 0 && !s.Pending) s.Status = "StatusUnconfirmed";
        foreach (var s in Sessions.Values.Where(s => s.Last < now.AddHours(-24)).ToArray()) Sessions.Remove(s.Id);
    }
    internal static MonitorState Load(DateTimeOffset now)
    {
        var state = new MonitorState();
        if (!File.Exists(state.FilePath)) return state;
        try
        {
            var snapshot = JsonSerializer.Deserialize<MonitorSnapshot>(File.ReadAllText(state.FilePath)) ?? throw new JsonException();
            if (snapshot.Version != 1 || snapshot.Sessions == null || snapshot.History == null || snapshot.Processed == null) throw new JsonException();
            if (snapshot.Sessions.Any(s => s == null || s.Id == null || s.Folder == null || s.Status == null || s.Requests == null || s.Tools == null ||
                s.Requests.Any(r => r == null || r.Id == null || r.Status == null || r.Tool == null || r.ToolId == null) ||
                s.Tools.Any(t => t == null || t.Tool == null || t.Id == null)) ||
                snapshot.History.Any(h => h == null || h.Id == null || h.SessionId == null || h.Folder == null || h.Status == null)) throw new JsonException();
            foreach (var s in snapshot.Sessions.Where(s => !string.IsNullOrEmpty(s.Id)))
            { s.Requests ??= []; s.Tools ??= []; s.Restored = true; state.Sessions[s.Id] = s; }
            state.History.AddRange(snapshot.History); state.processed.AddRange(snapshot.Processed.TakeLast(512));
            state.LastReceived = snapshot.LastReceived; state.Prune(now);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { state.LoadFailed = true; }
        return state;
    }
    internal void Save(DateTimeOffset now)
    {
        Prune(now);
        // Retain a broken snapshot for recovery before replacing it with a valid one.
        if (LoadFailed && File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".invalid-" + Guid.NewGuid().ToString("N") + ".bak");
        Storage.Atomic(FilePath, JsonSerializer.Serialize(new MonitorSnapshot
        { Sessions = Sessions.Values.ToList(), History = History, Processed = processed, LastReceived = LastReceived }, Storage.Json));
        LoadFailed = false;
    }
}
