using System.IO;
using System.Text.Json;

namespace ClaudeAlerta;

internal sealed class EventRules
{
    public bool Action { get; set; } = true;
    public bool Done { get; set; } = true;
    public bool Error { get; set; } = true;
    public bool Sound { get; set; } = true;
    internal bool Allows(string category) => category switch { "action" => Action, "done" => Done, "error" => Error, _ => false };
}
internal sealed class ProjectRules
{
    public string Folder { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Pinned { get; set; }
    public bool Muted { get; set; }
    public bool Action { get; set; } = true;
    public bool Done { get; set; } = true;
    public bool Error { get; set; } = true;
    public int ReminderSeconds { get; set; } = -2; // -2 inherits global, -1 progressive, 0 none
    internal bool Allows(string category) => category switch { "action" => Action, "done" => Done, "error" => Error, _ => false };
}
internal sealed class Preferences
{
    public bool Sound { get; set; } = true;
    public bool Popup { get; set; } = true;
    public int ReminderSeconds { get; set; } = -1;
    public string Language { get; set; } = L.DefaultLanguage;
    public string Profile { get; set; } = "normal";
    public DateTimeOffset ProfileSince { get; set; }
    public DateTimeOffset ResumeAt { get; set; }
    public int ResumeMinutes { get; set; } = 30;
    public Dictionary<string, EventRules> Profiles { get; set; } = Defaults();
    public Dictionary<string, ProjectRules> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    internal bool LoadFailed;
    static Dictionary<string, EventRules> Defaults() => new()
    {
        ["normal"] = new(), ["busy"] = new() { Done = false, Sound = false },
        ["silent"] = new() { Action = false, Done = false, Error = false, Sound = false }
    };
    internal static string ProjectKey(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        try { return Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant(); }
        catch { return folder.TrimEnd('\\', '/').ToUpperInvariant(); }
    }
    internal ProjectRules Project(string folder)
    {
        string key = ProjectKey(folder);
        if (!Projects.TryGetValue(key, out var rules)) Projects[key] = rules = new() { Folder = folder };
        return rules;
    }
    internal string ProjectName(SessionState session)
    {
        string? alias = Projects.GetValueOrDefault(ProjectKey(session.Folder))?.Alias;
        return string.IsNullOrWhiteSpace(alias) ? L.Project(session) : alias;
    }
    internal void Normalize()
    {
        Language = L.Normalize(Language);
        if (Profile is not ("normal" or "busy" or "silent")) Profile = "normal";
        if (ReminderSeconds is not (-1 or 0 or 30 or 60 or 120)) ReminderSeconds = -1;
        if (ResumeMinutes is not (0 or 5 or 15 or 30 or 60)) ResumeMinutes = 30;
        Profiles ??= Defaults(); foreach (var pair in Defaults()) if (!Profiles.TryGetValue(pair.Key, out var rule) || rule == null) Profiles[pair.Key] = pair.Value;
        Projects = new((Projects ?? []).Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Projects.Values) if (rule.ReminderSeconds is not (-2 or -1 or 0 or 30 or 60 or 120)) rule.ReminderSeconds = -2;
    }
    internal void SetProfile(string profile, int minutes, DateTimeOffset now)
    {
        if (profile != "normal" && (Profile == "normal" || ProfileSince == default)) ProfileSince = now;
        Profile = profile; ResumeAt = profile != "normal" && minutes > 0 ? now.AddMinutes(minutes) : default;
    }
    internal bool ResumeIfDue(DateTimeOffset now)
    {
        if (Profile == "normal" || ResumeAt == default || now < ResumeAt) return false;
        Profile = "normal"; ResumeAt = default; return true;
    }
    public static Preferences Load()
    {
        string path = Path.Combine(Storage.Root, "preferences.json");
        if (!File.Exists(path)) return new();
        try
        {
            var prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new JsonException();
            prefs.Normalize(); return prefs;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        { return new() { LoadFailed = true }; }
    }
    public void Save()
    {
        string path = Path.Combine(Storage.Root, "preferences.json");
        if (LoadFailed && File.Exists(path)) File.Copy(path, path + ".invalid-" + Guid.NewGuid().ToString("N") + ".bak");
        Storage.Atomic(path, JsonSerializer.Serialize(this, Storage.Json)); LoadFailed = false;
    }
}

internal static class NotificationPolicy
{
    internal static bool Allows(Preferences prefs, string folder, string category) =>
        prefs.Profile != "silent" && prefs.Profiles[prefs.Profile].Allows(category) &&
        (!prefs.Projects.TryGetValue(Preferences.ProjectKey(folder), out var rule) || (!rule.Muted && rule.Allows(category)));
    internal static int Interval(Preferences prefs, string folder) =>
        prefs.Projects.TryGetValue(Preferences.ProjectKey(folder), out var rule) && rule.ReminderSeconds != -2 ? rule.ReminderSeconds : prefs.ReminderSeconds;
    internal static TimeSpan Delay(int interval, int sent) => TimeSpan.FromSeconds(interval == -1 ? new[] { 120, 300, 900 }[Math.Clamp(sent, 0, 2)] : Math.Max(1, interval));
    internal static bool Due(Preferences prefs, SessionState s, PendingRequest r, DateTimeOffset now)
    {
        int interval = Interval(prefs, s.Folder);
        if (!Allows(prefs, s.Folder, "action") || (!prefs.Popup && !(prefs.Sound && prefs.Profiles[prefs.Profile].Sound)) || r.Reminders >= 3 || now < r.DeferredUntil) return false;
        if (interval == 0) return r.DeferredUntil != default && r.Reminders == 0 && now >= r.DeferredUntil;
        DateTimeOffset due = r.NextReminder == default ? r.Created + Delay(interval, 0) : r.NextReminder;
        return now >= due;
    }
    internal static void Sent(Preferences prefs, SessionState s, PendingRequest r, DateTimeOffset now)
    { r.Reminders++; r.NextReminder = now + Delay(Interval(prefs, s.Folder), r.Reminders); }
}
