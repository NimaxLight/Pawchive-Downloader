using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pawchive;

// Persisted states and progress stages remain language-independent internal values.
// Translate only presentation text; post titles, filenames, URLs and history keys are opaque.
public static class Locale
{
    static string language = "ru";
    public static string Language { get => Volatile.Read(ref language); set => Volatile.Write(ref language, value == "en" ? "en" : "ru"); }
    public static bool English => Language == "en";
    public static string DateTimeFormat => English ? "yyyy-MM-dd HH:mm" : "dd.MM.yyyy HH:mm";
    sealed record Pattern(Regex Regex, string English);
    static readonly Lazy<Dictionary<string, string>> strings = new(() =>
    {
        using var stream = typeof(Locale).Assembly.GetManifestResourceStream("Pawchive.Strings.en.json") ?? throw new InvalidOperationException("Missing English language resources.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    static readonly Lazy<Pattern[]> patterns = new(() => strings.Value.Where(p => p.Key.Contains("{0}"))
        .OrderByDescending(p => p.Key.Length).Select(p =>
        {
            var escaped = Regex.Escape(p.Key);
            escaped = Regex.Replace(escaped, @"\\\{(\d+)}", m => "(?<a" + m.Groups[1].Value + ">.*?)");
            return new Pattern(new Regex(@"\A" + escaped + @"\z", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking), p.Value);
        }).ToArray());
    public static string T(string? text)
    {
        if (text is null) return "";
        if (!English || !text.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')) return text;
        if (strings.Value.TryGetValue(text, out var translated)) return translated;
        foreach (var pattern in patterns.Value)
        {
            var match = pattern.Regex.Match(text); if (!match.Success) continue;
            return Regex.Replace(pattern.English, @"\{(\d+)}", m => match.Groups["a" + m.Groups[1].Value].Value);
        }
        return text;
    }
    public static string Error(string? text)
    {
        if (text is null) return "";
        if (!English) return text;
        var translated = T(text);
        // File-specific diagnostics prepend an opaque filename to an app-generated error.
        if (translated == text)
        {
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon >= 0) translated = text[..(colon + 2)] + T(text[(colon + 2)..]);
        }
        foreach (var pair in strings.Value.Where(p => !p.Key.Contains('{') && p.Key.Length >= 20))
            translated = translated.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        foreach (var source in new[] { "Пустой файл", "Неизвестный формат", "Пустые настройки", "Некорректные настройки" })
            if (translated.EndsWith(source, StringComparison.Ordinal)) translated = translated[..^source.Length] + strings.Value[source];
        return translated;
    }
}
