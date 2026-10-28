using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAlerta;

internal static class Storage
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Root => Environment.GetEnvironmentVariable("CLAUDE_ALERT_DATA") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeAlerta");
    internal static string Inbox => Path.Combine(Root, "inbox");
    internal static string Settings => Environment.GetEnvironmentVariable("CLAUDE_ALERT_SETTINGS") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    internal static void Atomic(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed record Signal(string Session, string Folder, string Event, string Tool,
    string ToolId, string Notification, DateTimeOffset At)
{
    internal static Signal Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Get(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "")[..Math.Min(v.GetString()!.Length, 2048)] : "";
        var session = Get("session_id");
        if (string.IsNullOrWhiteSpace(session)) throw new InvalidDataException("Evento sem sessão.");
        // Deliberately discard prompts, tool_input, command lines, messages and transcripts.
        return new(session, Get("cwd"), Get("hook_event_name"), Get("tool_name"),
            Get("tool_use_id"), Get("notification_type"), DateTimeOffset.UtcNow);
    }
    internal void Enqueue()
    {
        Directory.CreateDirectory(Storage.Inbox);
        Storage.Atomic(Path.Combine(Storage.Inbox, $"{At.UtcTicks:D19}-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(this, Storage.Json));
    }
}

internal static class HookInstaller
{
    internal const string Marker = "--claude-alert-hook";
    internal static readonly string[] Events = ["SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest",
        "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SessionEnd"];

    internal static bool IsOurs(JsonNode? hook) => hook is JsonObject obj &&
        obj["args"] is JsonArray args && args.Any(a => a?.ToString() == Marker) &&
        Path.GetFileName(obj["command"]?.ToString())?.Equals("ClaudeAlerta.exe", StringComparison.OrdinalIgnoreCase) == true;

    internal static JsonObject Merge(JsonObject root, string exe, bool remove)
    {
        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
            throw new InvalidDataException(L.T("HooksInvalid"));
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var name in Events)
        {
            if (hooks[name] is not null && hooks[name] is not JsonArray)
                throw new InvalidDataException(L.F("EventInvalid", name));
            var entries = hooks[name] as JsonArray ?? new JsonArray();
            foreach (var entry in entries.ToArray())
            {
                if (entry is not JsonObject obj || obj["hooks"] is not JsonArray handlers) continue;
                var owned = handlers.Where(IsOurs).ToArray();
                foreach (var handler in owned) handlers.Remove(handler);
                if (owned.Length > 0 && handlers.Count == 0) entries.Remove(entry);
            }
            if (!remove)
            {
                var entry = new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command", ["command"] = exe,
                        ["args"] = new JsonArray(Marker), ["timeout"] = 5, ["onFailure"] = "continue"
                    })
                };
                if (name == "Notification") entry["matcher"] =
                    "permission_prompt|idle_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input";
                entries.Add(entry);
            }
            if (entries.Count > 0) hooks[name] = entries;
            else hooks.Remove(name);
        }
        return root;
    }

    internal static string Install(bool remove = false)
    {
        string path = Storage.Settings;
        string? original = File.Exists(path) ? File.ReadAllText(path) : null;
        var root = original == null ? new JsonObject() : JsonNode.Parse(original,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            as JsonObject ?? throw new InvalidDataException(L.T("ConfigInvalid"));
        Merge(root, Environment.ProcessPath!, remove);
        if (original != null) File.Copy(path, path + ".claude-alerta-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-ffff") + ".bak", false);
        // Refuse to overwrite a concurrent edit made since the file was read.
        if ((File.Exists(path) ? File.ReadAllText(path) : null) != original)
            throw new IOException(L.T("ConfigChanged"));
        Storage.Atomic(path, root.ToJsonString(Storage.Json));
        return path;
    }

    internal static bool Installed()
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Storage.Settings), documentOptions:
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return Events.All(e => root?["hooks"]?[e] is JsonArray a && a.Any(x =>
                x?["hooks"] is JsonArray h && h.Any(v => IsOurs(v) &&
                    string.Equals(v?["command"]?.ToString(), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))));
        }
        catch { return false; }
    }
}
