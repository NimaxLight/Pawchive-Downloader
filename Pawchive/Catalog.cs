using System.Text.Json;

namespace Pawchive;

public class Catalog
{
    public int Version { get; set; } = 1;
    public Creator Creator { get; set; } = new("patreon", "0", "");
    public bool Complete { get; set; }
    public string CheckedAt { get; set; } = "";
    public string FullScanAt { get; set; } = "";
    public List<JsonElement> Posts { get; set; } = [];
    public Dictionary<string, string> ListingSignatures { get; set; } = [];
    public List<string> DirectPostIds { get; set; } = [];
    public static string FilePath(string root, Creator creator) => Path.Combine(root, ".pawchive", "catalog", Storage.Hash(creator.Key)[..24] + ".json");
    public static Catalog Load(string root, Creator creator)
    {
        var path = FilePath(root, creator);
        if (!File.Exists(path)) return new() { Creator = creator };
        try
        {
            var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path)) ?? throw new IOException("Пустой файл");
            if (catalog.Version != 1 || catalog.Creator.Key != creator.Key || catalog.Posts is null || catalog.DirectPostIds is null || catalog.Posts.Any(p => p.ValueKind != JsonValueKind.Object || Post.Str(p, "id").Length == 0))
                throw new IOException("Неизвестный формат");
            return catalog;
        }
        catch (Exception e) { throw new IOException($"Не удалось прочитать сохранённый список {creator.Name}. Файл не изменён: {path}. {e.Message}", e); }
    }
    public static List<Catalog> LoadAll(string root)
    {
        var directory = Path.Combine(root, ".pawchive", "catalog");
        if (!Directory.Exists(directory)) return [];
        var result = new List<Catalog>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var raw = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path)) ?? throw new IOException("Пустой список: " + path);
            result.Add(Load(root, raw.Creator));
        }
        return result;
    }
    public List<Post> ToPosts(Creator? creator = null) => Posts.Select(raw => new Post { Creator = creator ?? Creator, Raw = raw.Clone() }).ToList();
    public void Save(string root) => Storage.WriteJson(FilePath(root, Creator), this);
    public static async Task SavePost(string root, Post post, bool direct = false)
    {
        await Task.Run(() =>
        {
            var catalog = Load(root, post.Creator);
            catalog.Creator = post.Creator;
            catalog.Posts.RemoveAll(p => Post.Str(p, "id") == post.Id);
            catalog.Posts.Add(post.Raw.Clone());
            if (direct && !catalog.DirectPostIds.Contains(post.Id)) catalog.DirectPostIds.Add(post.Id);
            catalog.Save(root);
        });
    }
}
