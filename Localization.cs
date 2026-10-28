using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace ClaudeAlerta;

internal sealed record LanguageOption(string Code, string Name);

internal static class L
{
    internal const string DefaultLanguage = "pt-BR";
    internal static readonly LanguageOption[] Languages =
    [
        new("pt-BR", "Português"), new("en", "English"), new("es", "Español"),
        new("de", "Deutsch"), new("fr", "Français"), new("zh-Hans", "简体中文")
    ];
    static readonly Dictionary<string, Dictionary<string, string>> catalogs = [];
    internal static string Current { get; private set; } = DefaultLanguage;
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Current);
    internal static string Normalize(string? code) => Languages.FirstOrDefault(language =>
        string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? DefaultLanguage;

    internal static IReadOnlyDictionary<string, string> Catalog(string code)
    {
        code = Normalize(code);
        if (!catalogs.TryGetValue(code, out var catalog))
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ClaudeAlerta.Locales.{code}.json")
                ?? throw new InvalidDataException("Missing language catalog: " + code);
            catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidDataException("Invalid language catalog: " + code);
            catalogs[code] = catalog;
        }
        return catalog;
    }
    internal static string T(string key) => Catalog(Current).TryGetValue(key, out var value) ? value :
        Catalog(DefaultLanguage).TryGetValue(key, out var fallback) ? fallback : "[" + key + "]";
    internal static string F(string key, params object[] values) => string.Format(Culture, T(key), values);
    internal static void Apply(string? code)
    {
        Current = Normalize(code);
        CultureInfo.CurrentUICulture = Culture;
        if (Application.Current is null) return;
        foreach (var entry in Catalog(DefaultLanguage)) Application.Current.Resources["Loc." + entry.Key] = T(entry.Key);
    }
    internal static string Project(SessionState session) => string.IsNullOrEmpty(session.Folder) ? T("SessionGeneric") : session.Project;
    // Session states stay invariant: switching the UI language must never alter monitor behavior.
    internal static string Status(string status) => status switch
    {
        "Conectado" => T("StatusConnected"), "Trabalhando" => T("StatusWorking"),
        "Autorização necessária" => T("StatusPermission"), "Claude fez uma pergunta" => T("StatusQuestion"),
        "Plano aguardando aprovação" => T("StatusPlan"), "Resposta concluída" => T("StatusDone"),
        "Claude interrompido por erro" => T("StatusError"), "Resposta necessária" => T("StatusResponse"),
        _ => status
    };
}
