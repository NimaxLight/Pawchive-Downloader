using System.Globalization;
namespace Pawchive;
public enum DownloadMode { Missing, New, Problems, Picked }
public static class DownloadSelection
{
    public static bool InPeriod(Post p, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return true;
        if (from > to || p.Published.Length < 10 || !DateOnly.TryParseExact(p.Published[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        return (from is null || date >= from) && (to is null || date <= to);
    }
    public static bool IsProblem(Post p) => p.Error.Length > 0 || p.Warning.Length > 0 || p.State is "Частично скачан" or "Нужна распаковка" or "Пропущен / повторить" or "Не хватает файлов" or "Неполный пост" || p.State.Contains("Ошибка");
    public static List<Post> Build(IEnumerable<Post> ordered, HashSet<string> active, DownloadMode mode, HashSet<string> picked, DateOnly? from, DateOnly? to, bool recheck)
        => ordered.Where(p => active.Contains(p.Creator.Key) && InPeriod(p, from, to) && (p.State != "Скачан" || mode == DownloadMode.Picked || recheck && mode == DownloadMode.Missing))
            .Where(p => mode switch { DownloadMode.New => p.IsNew && p.State != "Скачан", DownloadMode.Problems => IsProblem(p), DownloadMode.Picked => picked.Contains(p.Key), _ => true }).ToList();
}
