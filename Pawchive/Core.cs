using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pawchive;

public record Creator(string Service, string Id, string Name, bool Selected = true)
{
    public string Key => Service + "/" + Id;
    public string Url => "https://pawchive.pw/" + Key.Replace("/", "/user/");
    public static Creator Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.Host.Equals("pawchive.pw", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Нужна ссылка вида https://pawchive.pw/patreon/user/12345");
        var m = Regex.Match(uri.AbsolutePath, @"^/([a-z0-9_-]+)/user/([a-zA-Z0-9_-]+)/?$");
        if (!m.Success) throw new ArgumentException("Вставьте ссылку на страницу автора, а не отдельного поста.");
        return new(m.Groups[1].Value, m.Groups[2].Value, m.Groups[2].Value);
    }
}

public class Settings
{
    public string Language { get; set; } = "ru";
    public string Theme { get; set; } = "system";
    public string AccentMode { get; set; } = "system";
    public string AccentColor { get; set; } = "#7D56A6";
    public bool DownloadPreviews { get; set; }
    public bool RecheckDownloaded { get; set; }
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "Downloads");
    public List<Creator> Creators { get; set; } = [];
}

public static class Storage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void WriteJson<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions));
    public static void WriteText(string path, string value, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false).GetBytes(value);
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (backup && File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(tmp, path, true);
    }
    public static string SafeName(string text, int max = 80)
    {
        text = Regex.Replace(text, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim().TrimEnd('.', ' ');
        if (text.Length > max) text = text[..max].TrimEnd('.', ' ');
        if (text.Length == 0) text = "untitled";
        if (Regex.IsMatch(text, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase)) text = "_" + text;
        return text;
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Inside(string root, string relative)
    {
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасный путь в истории скачиваний.");
        return path;
    }
}

public record Asset(string Key, string Name, string Url, bool Expiring = false, DateTimeOffset? Expires = null, bool Preview = false)
{
    public bool Expired => Expiring && Expires.HasValue && Expires.Value <= DateTimeOffset.UtcNow.AddSeconds(30);
}
public record SkippedAsset(string Key, string Name, string Reason);
public record PostAddress(Creator Creator, string Id)
{
    public static PostAddress Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.Equals("pawchive.pw", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Нужна ссылка на пост Pawchive: https://pawchive.pw/patreon/user/123/post/456");
        var match = Regex.Match(uri.AbsolutePath, @"^/([a-z0-9_-]+)/user/([a-zA-Z0-9_-]+)/post/([a-zA-Z0-9_-]+)/?$");
        if (!match.Success) throw new ArgumentException("Вставьте ссылку на конкретный пост: /платформа/user/ID/post/ID.");
        return new(new(match.Groups[1].Value, match.Groups[2].Value, match.Groups[2].Value), match.Groups[3].Value);
    }
}
public record MediaProgress(int Slot, string Name, long Received, long? Total, int Finished, int Count, string Stage);

public class Post
{
    readonly object cacheLock = new();
    List<string>? savedLinks;
    List<Asset>? savedAssets;
    List<Asset>? savedPreviews;
    List<SkippedAsset>? savedSkipped;
    string? savedFingerprint;
    public required Creator Creator { get; init; }
    public required JsonElement Raw { get; init; }
    public string Id => Str(Raw, "id");
    public string Key => Creator.Key + "/" + Id;
    public string Title => Str(Raw, "title");
    public string Published => Str(Raw, "published");
    public string Content => Str(Raw, "content");
    public string Url => Creator.Url + "/post/" + Id;
    public bool Incomplete => IsFalse("has_full") || IsFalse("detail_fetched");
    bool IsFalse(string key) => Raw.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.False;
    public string Error { get; set; } = "";
    public string Warning { get; set; } = "";
    public string State { get; set; } = "Не скачан";
    public static string Str(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    static readonly Regex Attrs = new(@"(?<key>href|src|poster)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BareUrls = new(@"https?://[^\s<>""'\u0000-\u001f]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static string CleanHtml(string html) => Regex.Replace(html, @"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    public List<string> Links()
    {
        lock (cacheLock) return savedLinks ??= ExtractLinks();
    }
    List<string> ExtractLinks()
    {
        var links = new HashSet<string>(StringComparer.Ordinal);
        void Add(string value)
        {
            value = WebUtility.HtmlDecode(value.Trim());
            if (value.StartsWith('#') || value.Length == 0) return;
            if (Uri.TryCreate(new Uri(Url), value, out var uri) && uri.Scheme is "https" or "http") links.Add(uri.AbsoluteUri);
        }
        var html = CleanHtml(Content);
        foreach (Match m in Attrs.Matches(html)) Add(m.Groups["value"].Value);
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        foreach (Match m in BareUrls.Matches(text)) Add(TrimUrl(m.Value));
        void Visit(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) Visit(p.Value);
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Visit(item);
            else if (e.ValueKind == JsonValueKind.String)
            {
                var value = e.GetString() ?? "";
                foreach (Match m in Attrs.Matches(value)) Add(m.Groups["value"].Value);
                foreach (Match m in BareUrls.Matches(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")))) Add(TrimUrl(m.Value));
            }
        }
        if (Raw.TryGetProperty("embed", out var embed)) Visit(embed);
        return links.Order(StringComparer.Ordinal).ToList();
    }
    static string TrimUrl(string url)
    {
        url = url.TrimEnd('.', ',', ';', '!', '。', '，', '；');
        while (url.EndsWith(')') && url.Count(c => c == ')') > url.Count(c => c == '(')) url = url[..^1];
        return url;
    }
    public List<Asset> Assets()
    {
        lock (cacheLock) return savedAssets ??= ExtractAssets();
    }
    public List<Asset> Assets(bool includePreviews) => includePreviews ? Assets().Concat(PreviewAssets()).ToList() : Assets();
    public List<Asset> PreviewAssets()
    {
        lock (cacheLock)
        {
            if (savedPreviews is not null) return savedPreviews;
            var previews = new Dictionary<string, Asset>(StringComparer.Ordinal);
            void Add(string url, string name = "")
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Thumbnail(uri)) return;
                string identity = uri.AbsolutePath.StartsWith("/thumbnail/", StringComparison.OrdinalIgnoreCase) ? uri.AbsolutePath[10..] : uri.AbsolutePath;
                string key = "preview:" + identity;
                if (name.Length == 0) name = Uri.UnescapeDataString(uri.Segments.Last());
                previews.TryAdd(key, new(key, name, uri.AbsoluteUri, Preview: true));
            }
            void Descriptor(JsonElement file)
            {
                string path = Str(file, "path");
                if (path.Length == 0) return;
                var uri = new Uri(AssetUrl(path, Str(file, "server")));
                string name = Str(file, "name");
                if (Thumbnail(uri)) { Add(uri.AbsoluteUri, name); return; }
                // Pawchive's page uses this thumbnail route for stored image paths.
                if (Regex.IsMatch(uri.AbsolutePath, @"\.(jpe?g|png|gif|webp|bmp|avif|tiff?)$", RegexOptions.IgnoreCase) && uri.AbsolutePath.StartsWith("/data/", StringComparison.Ordinal))
                    Add("https://img.pawchive.pw/thumbnail" + uri.AbsolutePath, name);
            }
            if (Raw.TryGetProperty("file", out var first) && first.ValueKind == JsonValueKind.Object) Descriptor(first);
            if (Raw.TryGetProperty("attachments", out var list) && list.ValueKind == JsonValueKind.Array) foreach (var file in list.EnumerateArray()) Descriptor(file);
            foreach (var link in Links()) Add(link);
            return savedPreviews = previews.Values.OrderBy(a => a.Key, StringComparer.Ordinal).ToList();
        }
    }
    public List<SkippedAsset> SkippedAssets()
    {
        lock (cacheLock) { Assets(); return savedSkipped ??= []; }
    }
    List<Asset> ExtractAssets()
    {
        var result = new Dictionary<string, Asset>(StringComparer.Ordinal);
        var priorities = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = new Dictionary<string, SkippedAsset>(StringComparer.Ordinal);
        void Add(JsonElement file)
        {
            var path = Str(file, "path");
            if (string.IsNullOrWhiteSpace(path)) return;
            var server = Str(file, "server");
            var name = Str(file, "name");
            var regularUrl = AssetUrl(path, server);
            if (name.Length == 0) name = Uri.UnescapeDataString(new Uri(regularUrl).Segments.Last());
            var key = new Uri(regularUrl).AbsolutePath;
            var previewOnly = file.TryGetProperty("preview_only", out var preview) && preview.ValueKind == JsonValueKind.True;
            var fullUrl = Str(file, "temp_url");
            var hasFullUrl = Uri.TryCreate(fullUrl, UriKind.Absolute, out var fullUri) && FullFile(fullUri);
            var regularUri = new Uri(regularUrl);
            bool explicitServer = server.Length > 0 || Uri.TryCreate(path, UriKind.Absolute, out var direct) && direct.Scheme == "https";
            string selected;
            int priority;
            // preview_only describes the local stored copy. A temp_url may still
            // point to the full-size image and must not be treated as a thumbnail.
            if (explicitServer && FullFile(regularUri)) { selected = regularUrl; priority = 3; }
            else if (hasFullUrl) { selected = fullUri!.AbsoluteUri; priority = 2; }
            else if ((!previewOnly || IsRelay(regularUri)) && FullFile(regularUri)) { selected = regularUrl; priority = explicitServer ? 3 : 1; }
            else
            {
                skipped.TryAdd(key, new(key, name, "Доступна только миниатюра: адрес полной версии сейчас не предоставлен."));
                return;
            }
            var selectedUri = new Uri(selected);
            bool expiring = selected == fullUrl || hasFullUrl && selected == fullUri!.AbsoluteUri || IsRelay(selectedUri);
            DateTimeOffset? expires = expiring && DateTimeOffset.TryParse(Str(file, "temp_expires"), out var expiry) ? expiry : null;
            if (expiring)
            {
                var match = Regex.Match(selectedUri.Query, @"(?:^|[?&])e=(\d+)(?:&|$)");
                if (match.Success && long.TryParse(match.Groups[1].Value, out var seconds))
                {
                    try { var signedExpiry = DateTimeOffset.FromUnixTimeSeconds(seconds); if (expires is null || signedExpiry < expires) expires = signedExpiry; }
                    catch (ArgumentOutOfRangeException) { }
                }
            }
            if (!priorities.TryGetValue(key, out var previousPriority) || priority > previousPriority)
            { result[key] = new(key, name, selected, expiring, expires); priorities[key] = priority; }
        }
        if (Raw.TryGetProperty("file", out var first) && first.ValueKind == JsonValueKind.Object) Add(first);
        if (Raw.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            foreach (var f in attachments.EnumerateArray()) Add(f);
        // Embedded originals hosted by Pawchive are files too; external links are saved only.
        foreach (var link in Links())
        {
            var uri = new Uri(link);
            if (FullFile(uri) && (uri.AbsolutePath.StartsWith("/data/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/f/", StringComparison.Ordinal)) && (!skipped.ContainsKey(uri.AbsolutePath) || !uri.Host.Equals("pawchive.pw", StringComparison.OrdinalIgnoreCase)) && !result.Values.Any(a => a.Url == uri.AbsoluteUri))
            {
                var key = uri.AbsolutePath;
                result.TryAdd(key, new(key, Uri.UnescapeDataString(uri.Segments.Last()), link, IsRelay(uri)));
            }
        }
        // The same path may appear twice: a preview descriptor and a real file
        // descriptor with a permanent server. Prefer the real attachment.
        foreach (var key in result.Keys) skipped.Remove(key);
        savedSkipped = skipped.Values.OrderBy(a => a.Key, StringComparer.Ordinal).ToList();
        return result.Values.OrderBy(a => a.Key, StringComparer.Ordinal).ToList();
    }
    public static bool Trusted(Uri uri) => uri.Scheme == "https" && (uri.Host.Equals("pawchive.pw", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".pawchive.pw", StringComparison.OrdinalIgnoreCase));
    public static bool IsRelay(Uri uri) => Regex.IsMatch(uri.Host, @"^t\d+\.pawchive\.pw$", RegexOptions.IgnoreCase) || uri.AbsolutePath.StartsWith("/f/", StringComparison.OrdinalIgnoreCase);
    public static bool FullFile(Uri uri) => Trusted(uri)
        && !uri.Host.Equals("img.pawchive.pw", StringComparison.OrdinalIgnoreCase)
        && !uri.AbsolutePath.Contains("/thumbnail/", StringComparison.OrdinalIgnoreCase);
    public static bool Thumbnail(Uri uri) => Trusted(uri) && (uri.Host.Equals("img.pawchive.pw", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("/thumbnail/", StringComparison.OrdinalIgnoreCase))
        && !uri.AbsolutePath.StartsWith("/static/", StringComparison.OrdinalIgnoreCase);
    public static string AssetUrl(string path, string server)
    {
        Uri uri;
        if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) uri = new(path);
        else
        {
            if (string.IsNullOrWhiteSpace(server)) server = "https://pawchive.pw";
            if (!path.StartsWith("/data/", StringComparison.Ordinal)) path = "/data/" + path.TrimStart('/');
            uri = new(new Uri(server.TrimEnd('/') + "/"), path);
        }
        if (!Trusted(uri)) throw new IOException("Неизвестный сервер вложения: " + uri.Host);
        return uri.AbsoluteUri;
    }
    public string Fingerprint()
    {
        lock (cacheLock) return savedFingerprint ??= Storage.Hash(JsonSerializer.Serialize(new { Title, Published, Content, Assets = Assets().Select(a => new { a.Key, a.Name }), Links = Links() }));
    }
    public string PlainText() => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(CleanHtml(Content), @"</?(p|div|br|li|h[1-6])\b[^>]*>", "\r\n", RegexOptions.IgnoreCase), "<[^>]+>", ""));
}

public class FileEntry
{
    public string RelativePath { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public string ExtractedFolder { get; set; } = "";
    public List<ExtractedFile> ExtractedFiles { get; set; } = [];
    public bool ExtractionComplete { get; set; }
    public string ExtractionError { get; set; } = "";
}
public class PostEntry
{
    public string Author { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public bool Complete { get; set; }
    public string CompletedAt { get; set; } = "";
    public string Error { get; set; } = "";
    public Dictionary<string, FileEntry> Files { get; set; } = [];
    public int ExpectedFiles { get; set; }
    public Dictionary<string, FileFailure> FileErrors { get; set; } = [];
    public List<SkippedAsset> SkippedFiles { get; set; } = [];
}
public record FileFailure(string Name, string Url, string Message, int? StatusCode);
public class History
{
    public const string SkippedMessage = "Пост пропущен пользователем. Следующий запуск попробует скачать его снова.";
    [System.Text.Json.Serialization.JsonIgnore]
    public object SyncRoot { get; } = new();
    public int Version { get; set; } = 1;
    public Dictionary<string, PostEntry> Posts { get; set; } = [];
    public static History Load(string root)
    {
        var path = Path.Combine(root, ".pawchive", "history.json");
        if (!File.Exists(path)) return new();
        try
        {
            var history = JsonSerializer.Deserialize<History>(File.ReadAllText(path)) ?? throw new IOException("Пустая история.");
            if (history.Version != 1 || history.Posts == null) throw new IOException("Неизвестный формат истории.");
            return history;
        }
        catch (Exception e) { throw new IOException("Не удалось прочитать историю. Она не перезаписана. Проверьте .pawchive/history.json и резервную копию .bak. " + e.Message, e); }
    }
    public void Save(string root) { lock (SyncRoot) Storage.WriteJson(Path.Combine(root, ".pawchive", "history.json"), this); }
    public static bool Present(string root, FileEntry f)
    {
        try { var info = new FileInfo(Storage.Inside(root, f.RelativePath)); return info.Exists && info.Length == f.Length; }
        catch { return false; }
    }
    public string State(string root, Post post, bool includePreviews = false)
    {
        if (!string.IsNullOrEmpty(post.Error)) return "Ошибка";
        if (post.Incomplete) return "Неполный пост";
        if (!Posts.TryGetValue(post.Key, out var entry)) return "Не скачан";
        if (!entry.Complete && entry.Error == SkippedMessage) return "Пропущен / повторить";
        if (entry.FileErrors.Count > 0) return "Частично скачан";
        if (!entry.Complete && entry.Files.Values.Any(f => f.ExtractionError.Length > 0)) return "Нужна распаковка";
        if (entry.Error.Length > 0) return "Ошибка / повторить";
        if (!entry.Complete) return "Частично скачан";
        if (entry.Fingerprint != post.Fingerprint()) return "Пост обновлён";
        var assets = post.Assets(includePreviews);
        if (assets.Any(a => !entry.Files.TryGetValue(a.Key, out var f) || !Present(root, f))) return "Не хватает файлов";
        if (assets.Any(a => Archives.IsArchive(a.Name) && !Archives.Present(root, entry.Files[a.Key]))) return "Нужна распаковка";
        foreach (var name in new[] { "post.txt", "post.json" })
            if (!File.Exists(Storage.Inside(root, Path.Combine(entry.Folder, name)))) return "Не хватает файлов";
        return "Скачан";
    }
}

public class Api : IDisposable
{
    // Shared across Api objects: the permit covers the entire response body and retries.
    static readonly SemaphoreSlim mediaSlots = new(2, 2);
    readonly HttpClient client;
    readonly TimeSpan spacing;
    public Api(HttpMessageHandler? handler = null, TimeSpan? spacing = null)
    {
        client = handler is null ? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) : new(handler);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PawchiveDownloader/1.8");
        client.DefaultRequestHeaders.Referrer = new Uri("https://pawchive.pw/");
        this.spacing = spacing ?? TimeSpan.FromMilliseconds(300);
    }
    async Task<HttpResponseMessage> Send(string url, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode) return response;
                var code = (int)response.StatusCode;
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                response.Dispose();
                if ((code == 429 || code >= 500) && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay?.TotalSeconds ?? (2 << attempt), 1, 60)), token);
                    continue;
                }
                var hint = code == 403 ? " Сервер ограничил доступ. Повторите позже." : code == 404 ? " Пост или файл отсутствует на сервере." : "";
                throw new HttpRequestException($"HTTP {code}.{hint}", null, (HttpStatusCode)code);
            }
            catch (Exception e) when ((e is HttpRequestException { StatusCode: null } || e is OperationCanceledException && !token.IsCancellationRequested) && attempt < 3)
            { await Task.Delay(TimeSpan.FromSeconds(2 << attempt), token); }
            catch (OperationCanceledException e) when (!token.IsCancellationRequested)
            { throw new IOException("Сервер не ответил вовремя. Повторите загрузку.", e); }
        }
    }
    public async Task<JsonElement> Json(string path, CancellationToken token)
    {
        await Task.Delay(spacing, token);
        using var response = await Send("https://pawchive.pw/api/v1/" + path, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        string text;
        try { text = await response.Content.ReadAsStringAsync(timeout.Token); }
        catch (OperationCanceledException e) when (!token.IsCancellationRequested)
        { throw new IOException("Сервер не успел передать данные постов. Повторите проверку.", e); }
        try { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        catch (JsonException e) { throw new IOException("API вернул страницу вместо данных. Попробуйте позже.", e); }
    }
    public async Task<Creator> Profile(Creator creator, CancellationToken token)
    {
        var json = await Json(creator.Service + "/user/" + creator.Id + "/profile", token);
        var name = Post.Str(json, "name");
        return name.Length == 0 ? creator : creator with { Name = name };
    }
    public async Task<List<Post>> Scan(Creator creator, Action<int> progress, CancellationToken token)
    {
        var posts = new Dictionary<string, Post>();
        var pageSignatures = new HashSet<string>();
        for (var offset = 0; ; offset += 50)
        {
            var page = await Json(creator.Service + "/user/" + creator.Id + "?o=" + offset, token);
            if (page.ValueKind != JsonValueKind.Array) throw new IOException("Неожиданный формат списка постов.");
            if (page.GetArrayLength() == 0) break;
            var signature = string.Join(",", page.EnumerateArray().Select(p => Post.Str(p, "id")));
            if (!pageSignatures.Add(signature)) throw new IOException("Сервер повторяет одну страницу. Проверка автора не завершена.");
            foreach (var raw in page.EnumerateArray())
            {
                var post = new Post { Creator = creator, Raw = raw.Clone() };
                if (string.IsNullOrWhiteSpace(post.Id)) throw new IOException("Пост без идентификатора в ответе API.");
                if (post.Incomplete)
                {
                    try { post = await Detail(post, token); }
                    catch (Exception e) when (e is not OperationCanceledException) { post.Error = e.Message; }
                }
                posts[post.Key] = post;
            }
            progress(posts.Count);
            // Pawchive uses fixed offsets of 50. Continue until an empty page,
            // including if the last page contains exactly fifty posts.
        }
        return posts.Values.ToList();
    }
    public async Task<Catalog> Discover(Creator creator, string root, bool full, Action<int> progress, CancellationToken token)
    {
        var catalog = await Task.Run(() => Catalog.Load(root, creator), token);
        var old = catalog.Posts.ToDictionary(p => Post.Str(p, "id"));
        var found = new Dictionary<string, JsonElement>();
        var listingSignatures = new Dictionary<string, string>();
        var signatures = new HashSet<string>();
        bool quick = !full && catalog.Complete;
        bool reachedEnd = false, unchangedBoundary = false;
        int offset = 0;
        while (!reachedEnd && !unchangedBoundary)
        {
            // Only two metadata requests in flight, independent of the media queue.
            var pages = await Task.WhenAll(Json(creator.Service + "/user/" + creator.Id + "?o=" + offset, token),
                Json(creator.Service + "/user/" + creator.Id + "?o=" + (offset + 50), token));
            foreach (var page in pages)
            {
                if (page.ValueKind != JsonValueKind.Array) throw new IOException("Неожиданный формат списка постов.");
                if (page.GetArrayLength() == 0) { reachedEnd = true; break; }
                var signature = string.Join(",", page.EnumerateArray().Select(p => Post.Str(p, "id")));
                if (!signatures.Add(signature)) throw new IOException("Сервер повторяет страницу. Сохранённый список не изменён.");
                bool unchanged = true;
                foreach (var raw in page.EnumerateArray())
                {
                    var id = Post.Str(raw, "id");
                    if (id.Length == 0) throw new IOException("Пост без идентификатора в ответе API.");
                    found[id] = raw.Clone();
                    var stamp = Storage.Hash(raw.GetRawText()); listingSignatures[id] = stamp;
                    var same = catalog.ListingSignatures.TryGetValue(id, out var oldStamp) ? oldStamp == stamp : old.TryGetValue(id, out var previous) && JsonElement.DeepEquals(previous, raw);
                    if (!same) unchanged = false;
                    // Keep a previously fetched full post if the lightweight listing
                    // has not changed. Detail requests must not invalidate the boundary.
                    if (same && old.TryGetValue(id, out var fullPost)) found[id] = fullPost;
                }
                progress(found.Count);
                if (quick && unchanged) { unchangedBoundary = true; break; }
            }
            offset += 100;
        }
        // Commit only a completed scan. A cancelled/failed refresh retains the
        // previous catalog and never passes an incomplete list off as complete.
        if (quick && unchangedBoundary)
        {
            foreach (var pair in old) found.TryAdd(pair.Key, pair.Value);
            foreach (var pair in catalog.ListingSignatures) listingSignatures.TryAdd(pair.Key, pair.Value);
        }
        catalog.Creator = creator;
        foreach (var id in catalog.DirectPostIds)
            if (!found.ContainsKey(id) && old.TryGetValue(id, out var directPost)) found[id] = directPost;
        catalog.Posts = found.Values.OrderByDescending(p => Post.Str(p, "published"), StringComparer.Ordinal).ToList();
        catalog.ListingSignatures = listingSignatures;
        catalog.Complete = true;
        catalog.CheckedAt = DateTimeOffset.Now.ToString("O");
        if (reachedEnd) catalog.FullScanAt = catalog.CheckedAt;
        token.ThrowIfCancellationRequested();
        await Task.Run(() => catalog.Save(root), token);
        return catalog;
    }
    public async Task<Post> Detail(Post post, CancellationToken token)
    {
        var json = await Json(post.Creator.Service + "/user/" + post.Creator.Id + "/post/" + post.Id, token);
        if (json.TryGetProperty("post", out var nested)) json = nested;
        if (Post.Str(json, "id") != post.Id) throw new IOException("API вернул другой пост.");
        return new Post { Creator = post.Creator, Raw = json.Clone() };
    }
    public async Task<Post> GetPost(PostAddress address, CancellationToken token)
    {
        Creator creator;
        try { creator = await Profile(address.Creator, token); }
        catch (Exception e) when (e is not OperationCanceledException) { creator = address.Creator; }
        return await Detail(new Post { Creator = creator, Raw = JsonSerializer.SerializeToElement(new { id = address.Id }) }, token);
    }
    public async Task<FileEntry> Download(Asset asset, string root, string relative, Action<long, long?> progress, CancellationToken token)
    {
        if (!(asset.Preview ? Post.Thumbnail(new Uri(asset.Url)) : Post.FullFile(new Uri(asset.Url)))) throw new IOException("Тип адреса не соответствует файлу: превью и полные версии скачиваются отдельно.");
        await mediaSlots.WaitAsync(token);
        try { return await DownloadCore(asset, root, relative, progress, token); }
        finally { mediaSlots.Release(); }
    }
    async Task<FileEntry> DownloadCore(Asset asset, string root, string relative, Action<long, long?> progress, CancellationToken token)
    {
        if (!Post.Trusted(new Uri(asset.Url))) throw new IOException("Неизвестный сервер вложения.");
        var destination = Storage.Inside(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        try
        {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await Send(asset.Url, token);
                if (response.RequestMessage?.RequestUri is Uri finalUri && !(asset.Preview ? Post.Thumbnail(finalUri) : Post.FullFile(finalUri)))
                    throw new InvalidDataException("Сервер перенаправил загрузку на миниатюру или посторонний адрес. Полная версия файла не получена.");
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (type.Contains("text/html", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(asset.Name).Equals(".html", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(asset.Name).Equals(".htm", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Сервер вернул HTML-страницу вместо файла.");
                var expected = response.Content.Headers.ContentLength;
                progress(0, expected);
                if (response.Content.Headers.ContentRange != null) throw new IOException("Сервер вернул неполный диапазон файла.");
                await using var input = await response.Content.ReadAsStreamAsync(token);
                long received = 0;
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                {
                    var buffer = new byte[131072];
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                        idle.CancelAfter(TimeSpan.FromSeconds(60));
                        var n = await input.ReadAsync(buffer, idle.Token);
                        if (n == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, n), token);
                        hasher.AppendData(buffer, 0, n);
                        received += n;
                        progress(received, expected);
                    }
                    await output.FlushAsync(token);
                    output.Flush(true);
                }
                if (expected.HasValue && received != expected.Value) throw new IOException("Файл скачался не полностью.");
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination, true);
                return new() { RelativePath = relative, Length = received, Sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant() };
            }
            catch (Exception e) when ((e is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } || e is HttpRequestException h && (int?)h.StatusCode >= 500 || e is IOException || e is OperationCanceledException && !token.IsCancellationRequested) && attempt < 2 && !token.IsCancellationRequested)
            { await Task.Delay(TimeSpan.FromSeconds(2 << attempt), token); }
            catch (OperationCanceledException e) when (!token.IsCancellationRequested)
            { throw new IOException("Сервер перестал передавать файл. Повторите загрузку.", e); }
        }
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public void Dispose() => client.Dispose();
}

public class Downloader(Api api, string root, History history, bool includePreviews = false)
{
    public Task SavePost(Post post, Action<string> status, Action<long, long?> progress, CancellationToken token) =>
        SavePost(post, status, p => progress(p.Received, p.Total), token);
    public async Task SavePost(Post post, Action<string> status, Action<MediaProgress> progress, CancellationToken token)
    {
        if (post.Incomplete || post.Error.Length > 0) post = await api.Detail(post, token);
        if (post.Incomplete && (!includePreviews || post.PreviewAssets().Count == 0)) throw new IOException("На сайте доступен неполный пост. Он не будет отмечен скачанным.");
        var assets = post.Assets(includePreviews);
        if (!history.Posts.TryGetValue(post.Key, out var entry))
        {
            var authorFolder = Storage.SafeName(post.Creator.Name, 35) + "_" + post.Creator.Service + "_" + Storage.SafeName(post.Creator.Id, 24);
            var date = post.Published.Length >= 10 ? Storage.SafeName(post.Published[..10]) : "undated";
            entry = new() { Folder = Path.Combine(authorFolder, date + "_" + Storage.SafeName(post.Id, 30) + "_" + Storage.SafeName(post.Title, 45)) };
            lock (history.SyncRoot) history.Posts[post.Key] = entry;
        }
        entry.Author = post.Creator.Name;
        entry.Title = post.Title;
        entry.Url = post.Url;
        entry.Complete = false;
        entry.Error = "";
        entry.ExpectedFiles = assets.Count;
        entry.FileErrors.Clear();
        entry.SkippedFiles = post.SkippedAssets();
        history.Save(root);
        try
        {
            WriteReports(post, entry);
            int next = -1, finished = 0;
            var refreshLock = new object();
            Task<Post>? refreshed = null;
            async Task<Post> RefreshPost()
            {
                var current = await api.Detail(post, token);
                await Catalog.SavePost(root, current);
                return current;
            }
            async Task<Asset> RefreshAsset(Asset asset)
            {
                Task<Post> task;
                lock (refreshLock) task = refreshed ??= RefreshPost();
                var current = await task;
                return current.Assets(includePreviews).FirstOrDefault(a => a.Key == asset.Key)
                    ?? throw new IOException("Полная версия файла не предоставлена в актуальных данных поста. Миниатюра не будет скачана вместо неё.");
            }
            async Task<FileEntry> Fetch(Asset asset, string relative, Action<long, long?> report)
            {
                var current = asset.Expired ? await RefreshAsset(asset) : asset;
                try { return await api.Download(current, root, relative, report, token); }
                catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone || current.Expiring && e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    Asset? fresh;
                    try { fresh = await RefreshAsset(asset); }
                    catch (Exception refreshError) when (refreshError is not OperationCanceledException) { fresh = null; }
                    if (fresh is null || fresh.Url == current.Url) throw;
                    return await api.Download(fresh, root, relative, report, token);
                }
            }
            async Task Worker(int slot)
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= assets.Count) return;
                    token.ThrowIfCancellationRequested();
                    var asset = assets[index];
                    var displayName = asset.Preview ? "Превью: " + asset.Name : asset.Name;
                    FileEntry? existing;
                    lock (history.SyncRoot) entry.Files.TryGetValue(asset.Key, out existing);
                    if (existing != null && History.Present(root, existing))
                    {
                        progress(new(slot, displayName, existing.Length, existing.Length, Interlocked.Increment(ref finished), assets.Count, "Уже скачан"));
                        continue;
                    }
                    var ext = Storage.SafeName(Path.GetExtension(asset.Name), 15);
                    if (ext == "untitled") ext = "";
                    var stem = Storage.SafeName(Path.GetFileNameWithoutExtension(asset.Name), 48);
                    var relative = Path.Combine(entry.Folder, asset.Preview ? "previews" : "", stem + "_" + Storage.Hash(asset.Key)[..12] + ext);
                    progress(new(slot, displayName, 0, null, Volatile.Read(ref finished), assets.Count, "Подключение"));
                    FileEntry file;
                    try
                    {
                        file = await Fetch(asset, relative,
                            (received, total) => progress(new(slot, displayName, received, total, Volatile.Read(ref finished), assets.Count, "Скачивание")));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        var failure = new FileFailure(displayName, new Uri(asset.Url).GetLeftPart(UriPartial.Path), e.Message, e is HttpRequestException http ? (int?)http.StatusCode : null);
                        lock (history.SyncRoot) { entry.FileErrors[asset.Key] = failure; history.Save(root); }
                        progress(new(slot, displayName, 0, null, Volatile.Read(ref finished), assets.Count, "Ошибка"));
                        continue;
                    }
                    lock (history.SyncRoot) { entry.Files[asset.Key] = file; history.Save(root); }
                    progress(new(slot, displayName, file.Length, file.Length, Interlocked.Increment(ref finished), assets.Count, "Готово"));
                }
            }
            // Both workers settle before marking failure/cancellation or releasing the library lock.
            await Task.WhenAll(Worker(0), Worker(1));
            token.ThrowIfCancellationRequested();
            var extractionErrors = new List<string>();
            foreach (var asset in assets.Where(a => Archives.IsArchive(a.Name)))
            {
                if (!entry.Files.TryGetValue(asset.Key, out var file) || !History.Present(root, file)) continue;
                if (Archives.Present(root, file)) continue;
                progress(new(0, asset.Name, 0, null, finished, assets.Count, "Распаковка"));
                status("Распаковываю: " + asset.Name);
                try
                {
                    await Archives.Extract(root, file, asset.Name, token);
                    history.Save(root);
                    progress(new(0, asset.Name, file.Length, file.Length, finished, assets.Count, "Распакован"));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    file.ExtractionError = e.Message; file.ExtractionComplete = false;
                    extractionErrors.Add(asset.Name + ": " + e.Message); history.Save(root);
                }
            }
            var problems = entry.FileErrors.Values.Select(f => f.Name + ": " + f.Message).Concat(extractionErrors.Select(e => "Распаковка " + e)).ToList();
            if (post.Incomplete) problems.Add("На сайте доступен неполный пост. Доступные файлы сохранены, но пост не отмечен скачанным.");
            entry.Fingerprint = post.Fingerprint();
            entry.Complete = problems.Count == 0;
            if (entry.Complete) entry.CompletedAt = DateTimeOffset.Now.ToString("O");
            entry.Error = string.Join("; ", problems);
            WriteReports(post, entry);
            history.Save(root);
            post.Error = ""; post.Warning = entry.Error;
            post.State = history.State(root, post, includePreviews);
        }
        catch (Exception e)
        {
            entry.Error = e is OperationCanceledException ? "Остановлено пользователем" : e.Message;
            entry.Complete = false;
            history.Save(root);
            try { WriteReports(post, entry); } catch (IOException) { }
            throw;
        }
    }
    public void MarkSkipped(Post post)
    {
        if (!history.Posts.TryGetValue(post.Key, out var entry)) return;
        entry.Complete = false;
        entry.Error = History.SkippedMessage;
        history.Save(root); WriteReports(post, entry);
    }
    void WriteReports(Post post, PostEntry entry)
    {
        var folder = Storage.Inside(root, entry.Folder); Directory.CreateDirectory(folder);
        var problems = entry.FileErrors.Values.Select(f => f.Name + ": " + f.Message)
            .Concat(entry.Files.Values.Where(f => f.ExtractionError.Length > 0).Select(f => "Распаковка " + Path.GetFileName(f.RelativePath) + ": " + f.ExtractionError)).Distinct().ToList();
        if (entry.Error.Length > 0 && entry.Error != string.Join("; ", problems)) problems.Insert(0, entry.Error);
        string errors = problems.Count > 0 ? string.Join("\r\n", problems) : "Ошибок нет.";
        string skipped = entry.SkippedFiles.Count > 0 ? string.Join("\r\n", entry.SkippedFiles.Select(s => s.Name + ": " + Locale.T(s.Reason))) : Locale.T("Пропусков нет.");
        if (includePreviews) skipped += "\r\n" + Locale.T("Превью скачиваются отдельно в подпапку previews. Наличие превью не означает наличие оригинала.");
        Storage.WriteText(Path.Combine(folder, "post.txt"), Locale.T("ОШИБКИ") + "\r\n" + Locale.Error(errors) + "\r\n\r\n" + Locale.T("ПРОПУСКИ") + "\r\n" + skipped + "\r\n\r\n" + Locale.T("ССЫЛКИ") + "\r\n" + (post.Links().Count > 0 ? string.Join("\r\n", post.Links()) : Locale.T("Ссылок нет.")) + "\r\n\r\n" + Locale.T("ПОСТ") + "\r\n" + post.Title + "\r\n" + post.Url + "\r\n" + post.Published + "\r\n\r\n" + post.PlainText(), false);
        Storage.WriteText(Path.Combine(folder, "post.json"), JsonSerializer.Serialize(post.Raw, Storage.JsonOptions), false);
        // Only obsolete reports owned by this app are removed, after the combined report is committed.
        foreach (var name in new[] { "links.txt", "errors.txt", "skipped.txt", "links.txt.bak", "errors.txt.bak", "skipped.txt.bak", "post.txt.bak" })
            File.Delete(Path.Combine(folder, name));
    }
}
