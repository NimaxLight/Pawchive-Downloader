using System.Net;
using System.Text;
using System.Text.Json;
using Pawchive;
using System.IO.Compression;

class TransferCounter
{
    public int Active, Max, Started;
    public void Enter()
    {
        var active = Interlocked.Increment(ref Active); Interlocked.Increment(ref Started);
        int previous;
        do { previous = Max; if (active <= previous) break; } while (Interlocked.CompareExchange(ref Max, active, previous) != previous);
    }
}
class SlowStream(TransferCounter counter) : MemoryStream(new byte[1024 * 1024])
{
    int disposed;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    { await Task.Delay(20, token); return await base.ReadAsync(buffer, token); }
    protected override void Dispose(bool disposing) { if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref counter.Active); base.Dispose(disposing); }
}
class SlowHandler(TransferCounter counter) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); counter.Enter();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(counter)) });
    }
}

class FakeHandler : HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> Calls = new();
    public required Func<HttpRequestMessage, HttpResponseMessage> Respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = request.RequestUri!.PathAndQuery;
        Calls.AddOrUpdate(key, 1, (_, count) => count + 1);
        return Task.FromResult(Respond(request));
    }
    public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    public static HttpResponseMessage File(string value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value)) };
}

static class Tests
{
    static int count;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
    static Creator creator = new("patreon", "305523", "Test / Author");
    static object Raw(string id = "1", bool extra = false, string title = "Test", bool complete = true) => new
    {
        id, title, published = "2026-11-10T10:00:00", content = "<p><a href='https://example.org/folder?a=1&amp;b=2#key'>download</a></p><p>https://example.com/a(b). https://example.net/file#secret</p><img src='/data/aa/bb/inline.png'>",
        file = new { name = "same.png", path = "/aa/bb/first.png", server = "https://n1.pawchive.pw" },
        attachments = extra ? new[] { new { name = "same.png", path = "/aa/bb/second.png", server = "https://n2.pawchive.pw" } } : [],
        embed = new { url = "https://example.org/embed#password" }, has_full = complete, detail_fetched = complete
    };
    static Post P(object raw) => new() { Creator = creator, Raw = JsonSerializer.SerializeToElement(raw) };
    static string Root(string name) { var path = Path.Combine(AppContext.BaseDirectory, "test-data", name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    static async Task Main(string[] args)
    {
        var textFixture = args.FirstOrDefault(a => a.StartsWith("--text-post="));
        if (textFixture is not null)
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(textFixture[12..]));
            var fixturePost = new Post { Creator = creator, Raw = fixture.RootElement.Clone() };
            Check(!fixturePost.Incomplete && fixturePost.Assets().Count == 0, "Actual API fixture is recognized as a complete text-only post");
            var fixtureRoot = Root("text-api-fixture"); var fixtureHistory = new History();
            using var fixtureApi = new Api(new FakeHandler { Respond = _ => throw new Exception("No HTTP request expected") }, TimeSpan.Zero);
            await new Downloader(fixtureApi, fixtureRoot, fixtureHistory).SavePost(fixturePost, _ => { }, (_, _) => { }, CancellationToken.None);
            Check(fixtureHistory.State(fixtureRoot, fixturePost) == "Скачан", "Actual API fixture saves successfully without requesting media");
            return;
        }
        if (args.Contains("--live")) { await Live(); return; }
        if (args.Contains("--catalog-live")) { await CatalogLive(); return; }
        if (args.Contains("--permanent-live")) { await PermanentLive(); return; }
        if (args.Contains("--full-photo-live")) { await FullPhotoLive(); return; }
        var p = P(Raw());
        Check(Creator.Parse(creator.Url).Key == creator.Key, "creator URL round-trip");
        bool bad = false; try { Creator.Parse("https://pawchive.pw.evil.test/patreon/user/123"); } catch { bad = true; }
        Check(bad, "reject lookalike host");
        Check(p.Links().Contains("https://example.org/folder?a=1&b=2#key"), "HTML decode and preserve query + fragment");
        Check(p.Links().Contains("https://example.com/a(b)"), "preserve balanced URL parentheses");
        Check(p.Links().Contains("https://example.org/embed#password"), "save embed links");
        Check(p.Assets().Count == 2 && p.Assets().Any(a => a.Url == "https://n1.pawchive.pw/data/aa/bb/first.png"), "original CDN + inline attachment");
        Check(!p.Assets().Any(a => a.Url.Contains("example.org")), "external links saved without fetching");
        Check(Storage.SafeName("CON.png") == "_CON.png" && !Storage.SafeName("../../evil").Contains('/'), "Windows filename safety");
        bad = false; try { Storage.Inside(Root("safe"), "../escape"); } catch { bad = true; }
        Check(bad, "reject history paths outside download root");

        var handler = new FakeHandler { Respond = r => FakeHandler.File(r.RequestUri!.AbsolutePath) };
        using var api = new Api(handler, TimeSpan.Zero);
        var root = Root("downloads"); var history = new History(); var downloader = new Downloader(api, root, history);
        await downloader.SavePost(p, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(history.State(root, p) == "Скачан", "complete only after all files + metadata");
        var entry = history.Posts[p.Key];
        Check(entry.Files.Count == 2 && entry.Files.Values.All(f => f.Sha256.Length == 64), "history stores individual files + SHA256");
        Check(File.ReadAllText(Path.Combine(root, entry.Folder, "post.txt")).Contains("#secret"), "combined post.txt preserves link fragment");
        Check(History.Load(root).State(root, p) == "Скачан", "history survives restart");
        var before = handler.Calls.Values.Sum();
        await new Downloader(api, root, History.Load(root)).SavePost(p, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == before, "repeat run does not download completed files");

        var updated = P(Raw(extra: true, title: "Updated title"));
        Check(history.State(root, updated) == "Пост обновлён", "detect edits and new attachment on old post");
        await downloader.SavePost(updated, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == before + 1, "updated post downloads only added attachment");
        Check(history.Posts[p.Key].Files.Values.Select(f => f.RelativePath).Distinct().Count() == 3, "duplicate original filenames do not overwrite");
        Check(history.Posts[p.Key].Folder == entry.Folder, "title edit preserves existing folder");
        var f = entry.Files.Values.First(); File.Delete(Path.Combine(root, f.RelativePath));
        Check(history.State(root, updated) == "Не хватает файлов", "detect manually deleted file");
        await downloader.SavePost(updated, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == before + 2, "restore only missing file");
        Check(new History().State(Root("different-root"), updated) == "Не скачан", "history isolated by download folder");

        var cancelRoot = Root("cancel"); var cancelHistory = new History();
        using var cts = new CancellationTokenSource();
        bad = false;
        try { await new Downloader(api, cancelRoot, cancelHistory).SavePost(updated, _ => { }, (_, _) => cts.Cancel(), cts.Token); }
        catch (OperationCanceledException) { bad = true; }
        Check(bad && !History.Load(cancelRoot).Posts[updated.Key].Complete, "cancelled post never marked complete");
        await new Downloader(api, cancelRoot, cancelHistory).SavePost(updated, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(cancelHistory.State(cancelRoot, updated) == "Скачан", "resume interrupted post on next click");

        var broken = new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.EndsWith("inline.png") ? new(HttpStatusCode.OK) { Content = new StringContent("<html>error</html>", Encoding.UTF8, "text/html") } : FakeHandler.File("original") };
        using var brokenApi = new Api(broken, TimeSpan.Zero);
        var brokenRoot = Root("failed-file"); var brokenHistory = new History();
        await new Downloader(brokenApi, brokenRoot, brokenHistory).SavePost(p, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(!History.Load(brokenRoot).Posts[p.Key].Complete && brokenHistory.Posts[p.Key].FileErrors.Count == 1, "HTML error page recorded as individual failed attachment");
        Check(brokenHistory.Posts[p.Key].Files.Count == 1 && brokenHistory.Posts[p.Key].Error.Length > 0, "failed post preserves successfully downloaded first file");
        broken.Respond = _ => FakeHandler.File("original");
        await new Downloader(brokenApi, brokenRoot, brokenHistory).SavePost(p, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(broken.Calls["/data/aa/bb/first.png"] == 1 && brokenHistory.State(brokenRoot, p) == "Скачан", "retry after error skips saved file and completes post");

        var linksOnly = P(new { id = "links", title = "Links only", content = "<a href='https://example.com/archive#key'>Archive</a>", file = new { }, attachments = Array.Empty<object>() });
        await downloader.SavePost(linksOnly, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(history.State(root, linksOnly) == "Скачан", "links-only posts are tracked");
        var textRaw = new { id = "text-only", title = "Text update", content = "<p>A complete text update.</p>", has_full = false, detail_fetched = true, file = new { }, attachments = Array.Empty<object>(), embed = new { }, shared_file = false, preview_state = "pending" };
        var textOnly = P(textRaw);
        Check(!textOnly.Incomplete && history.State(root, textOnly) == "Не скачан", "Fully fetched text-only post is not incomplete when has_full is false");
        var textHandler = new FakeHandler { Respond = _ => throw new Exception("Text-only save must not request media or fetch details again") };
        using var textApi = new Api(textHandler, TimeSpan.Zero);
        await new Downloader(textApi, root, history).SavePost(textOnly, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(history.State(root, textOnly) == "Скачан" && history.Posts[textOnly.Key].ExpectedFiles == 0, "Text-only posts save reports and complete with zero media requests");
        Check(File.ReadAllText(Path.Combine(root, history.Posts[textOnly.Key].Folder, "post.txt")).Contains("A complete text update."), "Text-only report preserves post content");
        var textCatalog = new Catalog { Creator = creator, Complete = true, Posts = [JsonSerializer.SerializeToElement(textRaw)] }; textCatalog.Save(root);
        Check(!Catalog.Load(root, creator).ToPosts().Single().Incomplete && History.Load(root).State(root, textOnly) == "Скачан", "Cached text-only posts remain complete after restart");
        Check(P(new { id = "unfetched-text", content = "Summary only", has_full = false, detail_fetched = false, file = new { }, attachments = Array.Empty<object>() }).Incomplete, "Unfetched text summaries still require post details");
        Check(P(new { id = "missing-media", content = "Text with unavailable original", has_full = false, detail_fetched = true, file = new { name = "missing.png", path = "/missing.png", preview_only = true } }).Incomplete, "Missing media with accompanying text stays incomplete");
        Check(P(new { id = "inline-media", content = "<p>Caption</p><img src='https://img.pawchive.pw/thumbnail/data/p.png'>", has_full = false, detail_fetched = true, file = new { } }).Incomplete, "Preview-only inline media is not misclassified as a text-only post");

        var corruptRoot = Root("corrupt"); Directory.CreateDirectory(Path.Combine(corruptRoot, ".pawchive"));
        var corruptFile = Path.Combine(corruptRoot, ".pawchive", "history.json"); File.WriteAllText(corruptFile, "{bad");
        bad = false; try { History.Load(corruptRoot); } catch (IOException) { bad = true; }
        Check(bad && File.ReadAllText(corruptFile) == "{bad", "corrupt history rejected and preserved");

        var paging = new FakeHandler { Respond = r => r.RequestUri!.Query switch
        {
            "?o=0" => FakeHandler.Json(Enumerable.Range(1, 50).Select(i => Raw(i.ToString()))),
            "?o=50" => FakeHandler.Json(new[] { Raw("51") }),
            _ => FakeHandler.Json(Array.Empty<object>())
        }};
        using var pagingApi = new Api(paging, TimeSpan.Zero);
        var list = await pagingApi.Scan(creator, _ => { }, CancellationToken.None);
        Check(list.Count == 51 && paging.Calls.Keys.Any(k => k.EndsWith("?o=100")), "scan all pages through empty page");
        var repeated = new FakeHandler { Respond = _ => FakeHandler.Json(new[] { Raw() }) };
        using var repeatedApi = new Api(repeated, TimeSpan.Zero);
        bad = false; try { await repeatedApi.Scan(creator, _ => { }, CancellationToken.None); } catch (IOException) { bad = true; }
        Check(bad, "repeated server page cannot masquerade as successful scan");
        var full = new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.Contains("/post/") ? FakeHandler.Json(Raw()) : FakeHandler.Json(r.RequestUri.Query == "?o=0" ? new[] { Raw(complete: false) } : []) };
        using var fullApi = new Api(full, TimeSpan.Zero);
        var resolved = await fullApi.Scan(creator, _ => { }, CancellationToken.None);
        Check(resolved.Count == 1 && !resolved[0].Incomplete, "incomplete listing resolved through post details");
        await NewFeatures();
        await CatalogChecks();
        await PartialAndTemporaryChecks();
        await BacklogChecks();
        var fixtureArg = args.FirstOrDefault(a => a.StartsWith("--fixtures="));
        if (fixtureArg != null) await ArchiveFixtures(fixtureArg[11..]);
        Console.WriteLine($"{count} assertions passed.");
    }
    static async Task BacklogChecks()
    {
        var post = P(new { id = "preview-feature", title = "Preview only", published = "2026-11-11", content = "<img loading='lazy' src='https://img.pawchive.pw/thumbnail/data/aa/image.jpg'><a href='https://example.com/folder#keep'>Link</a>", file = new { name = "image.jpg", path = "/aa/image.jpg", preview_only = true }, attachments = Array.Empty<object>() });
        Check(post.PreviewAssets().Count == 1 && post.PreviewAssets()[0].Key == "preview:/data/aa/image.jpg", "API and inline preview share stable identity");
        Check(post.Assets().Count == 0 && post.Assets(true).Count == 1 && post.Assets(true)[0].Preview, "preview opt-in never promotes thumbnail to original");
        var handler = new FakeHandler { Respond = _ => FakeHandler.File("preview-image") }; using var api = new Api(handler, TimeSpan.Zero);
        string root = Root("backlog"); var history = new History();
        await new Downloader(api, root, history).SavePost(post, _ => { }, (_, _) => { }, CancellationToken.None);
        var entry = history.Posts[post.Key]; string folder = Path.Combine(root, entry.Folder);
        Check(handler.Calls.IsEmpty && entry.Complete && entry.SkippedFiles.Count == 1, "disabled previews cause no media requests and retain skipped-original information");
        string report = File.ReadAllText(Path.Combine(folder, "post.txt"));
        Check(report.IndexOf("ОШИБКИ") < report.IndexOf("ПРОПУСКИ") && report.IndexOf("ПРОПУСКИ") < report.IndexOf("ССЫЛКИ") && report.IndexOf("ССЫЛКИ") < report.IndexOf("\r\nПОСТ\r\n") && report.Contains("#keep"), "combined report section order and link fragment preserved");
        foreach (var name in new[] { "links.txt", "errors.txt", "skipped.txt", "post.txt.bak", "links.txt.bak" }) File.WriteAllText(Path.Combine(folder, name), "legacy");
        File.WriteAllText(Path.Combine(folder, "my-notes.txt"), "keep my notes");
        await new Downloader(api, root, history).SavePost(post, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(!new[] { "links.txt", "errors.txt", "skipped.txt", "post.txt.bak", "links.txt.bak" }.Any(n => File.Exists(Path.Combine(folder, n))) && File.ReadAllText(Path.Combine(folder, "my-notes.txt")) == "keep my notes", "migration removes only obsolete app reports and keeps user text files");
        Check(history.State(root, post) == "Скачан" && history.State(root, post, true) == "Не хватает файлов", "enabling previews schedules missing preview on completed post");
        await new Downloader(api, root, history, true).SavePost(post, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == 1 && entry.Files.Keys.Single().StartsWith("preview:") && entry.Files.Values.Single().RelativePath.Contains("previews"), "preview is downloaded once into separate folder");
        Check(history.State(root, post, true) == "Скачан" && entry.SkippedFiles.Count == 1, "saved preview does not hide unavailable original");
        await new Downloader(api, root, history, true).SavePost(post, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == 1 && !File.Exists(Path.Combine(folder, "post.txt.bak")), "repeat download skips existing preview and leaves one app text report");
        var updated = P(new { id = post.Id, title = post.Title, published = post.Published, content = post.Content, file = new { name = "image.jpg", path = "/aa/image.jpg", server = "https://n2.pawchive.pw" }, attachments = Array.Empty<object>() });
        await new Downloader(api, root, history, true).SavePost(updated, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(handler.Calls.Values.Sum() == 2 && entry.Files.Count == 2 && entry.Files.Values.Select(f => f.RelativePath).Distinct().Count() == 2 && entry.SkippedFiles.Count == 0, "newly available original downloads beside preserved preview without re-downloading it");
        var slowCounter = new TransferCounter(); using var slowApi = new Api(new SlowHandler(slowCounter), TimeSpan.Zero);
        var cancelRoot = Root("cancel-cleanup"); var cancelHistory = new History(); using var cancel = new CancellationTokenSource();
        var cancelPost = P(new { id = "cancel-cleanup", title = "Cancellation", file = new { name = "large.bin", path = "/large.bin" } });
        bool stopped = false;
        try { await new Downloader(slowApi, cancelRoot, cancelHistory).SavePost(cancelPost, _ => { }, (long received, long? total) => { if (received > 0) cancel.Cancel(); }, cancel.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && !Directory.EnumerateFiles(cancelRoot, "*.part", SearchOption.AllDirectories).Any() && !cancelHistory.Posts[cancelPost.Key].Complete, "cancellation after body data removes partial file and does not complete post");
        Check(File.ReadAllText(Path.Combine(cancelRoot, cancelHistory.Posts[cancelPost.Key].Folder, "post.txt")).Contains("Остановлено пользователем"), "cancelled post has combined diagnostic report");
        using var rejected = new Api(new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) }, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(cancelRoot, "old.bin.part"), "unfinished");
        try { await rejected.Download(new("old", "old.bin", "https://n1.pawchive.pw/data/old.bin"), cancelRoot, "old.bin", (_, _) => { }, CancellationToken.None); } catch (HttpRequestException) { }
        Check(!File.Exists(Path.Combine(cancelRoot, "old.bin.part")), "failed retry removes its obsolete temporary file");
        var malicious = P(new { id = "untrusted-preview", content = "<img src='https://img.pawchive.pw.evil.test/thumbnail/data/a.jpg'>" });
        Check(malicious.PreviewAssets().Count == 0, "preview opt-in does not allow external or lookalike hosts");
        var incompleteRaw = new { id = "incomplete-preview", title = "Only preview available", has_full = false, detail_fetched = true, file = new { name = "preview.jpg", path = "/aa/incomplete.jpg", preview_only = true } };
        using var incompleteApi = new Api(new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.Contains("/post/") ? FakeHandler.Json(incompleteRaw) : FakeHandler.File("preview") }, TimeSpan.Zero);
        var incompleteRoot = Root("incomplete-preview"); var incompleteHistory = new History(); var incompletePost = P(incompleteRaw);
        await new Downloader(incompleteApi, incompleteRoot, incompleteHistory, true).SavePost(incompletePost, _ => { }, (_, _) => { }, CancellationToken.None);
        Check(incompleteHistory.Posts[incompletePost.Key].Files.Count == 1 && !incompleteHistory.Posts[incompletePost.Key].Complete, "available preview from incomplete post is saved without marking the post complete");
        var preserved = entry.Files["/data/aa/image.jpg"];
        var originalBytes = File.ReadAllBytes(Path.Combine(root, preserved.RelativePath));
        var extended = P(new { id = updated.Id, title = updated.Title, file = new { name = "image.jpg", path = "/aa/image.jpg", server = "https://n2.pawchive.pw" }, attachments = new[] { new { name = "new.bin", path = "/new.bin" } } });
        using var cancelNext = new CancellationTokenSource();
        try { await new Downloader(slowApi, root, history).SavePost(extended, _ => { }, (long received, long? total) => { if (received > 0 && total == 1048576) cancelNext.Cancel(); }, cancelNext.Token); } catch (OperationCanceledException) { }
        Check(File.ReadAllBytes(Path.Combine(root, preserved.RelativePath)).SequenceEqual(originalBytes) && !Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories).Any() && !entry.Complete, "cancelling a later attachment preserves completed original and removes only unfinished file");
        try
        {
            Locale.Language = "en";
            var englishPost = P(new { id = "english-report", title = "Русский заголовок", content = "<p>Русский текст</p><a href='https://example.org/folder#ключ'>Link</a>", file = new { name = "Ошибка.jpg", path = "/english/missing.jpg", server = "https://n1.pawchive.pw" }, attachments = new[] { new { name = "Превью.jpg", path = "/english/preview.jpg", preview_only = true } } });
            using var englishApi = new Api(new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.Contains("/post/") ? FakeHandler.Json(englishPost.Raw) : new HttpResponseMessage(HttpStatusCode.NotFound) }, TimeSpan.Zero);
            var englishRoot = Root("english-report"); var englishHistory = new History();
            await new Downloader(englishApi, englishRoot, englishHistory).SavePost(englishPost, _ => { }, (_, _) => { }, CancellationToken.None);
            string englishReport = File.ReadAllText(Path.Combine(englishRoot, englishHistory.Posts[englishPost.Key].Folder, "post.txt"));
            Check(englishReport.StartsWith("ERRORS\r\n") && englishReport.Contains("\r\nSKIPPED\r\n") && englishReport.Contains("\r\nLINKS\r\n") && englishReport.Contains("\r\nPOST\r\n"), "English combined report section labels");
            Check(englishReport.Contains("Only a thumbnail is available") && englishReport.Contains("The post or file is missing from the server"), "English report translates skipped reasons and download errors");
            Check(englishReport.Contains("Русский заголовок") && englishReport.Contains("Русский текст") && englishReport.Contains("Ошибка.jpg"), "Report translation preserves original post content and filenames");
            Check(englishHistory.State(englishRoot, englishPost) == "Частично скачан", "English language never changes internal history state codes");
        }
        finally { Locale.Language = "ru"; }
    }
    static async Task CatalogChecks()
    {
        var items = Enumerable.Range(1, 151).Select(i => Raw(i.ToString(), complete: false)).ToList();
        var handler = new FakeHandler { Respond = r =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("/post/")) throw new Exception("Scan must not fetch full post details");
            var offset = int.Parse(r.RequestUri.Query.Split('=')[1]);
            return FakeHandler.Json(items.Skip(offset).Take(50));
        }};
        using var api = new Api(handler, TimeSpan.Zero); var root = Root("catalog");
        var first = await api.Discover(creator, root, false, _ => { }, CancellationToken.None);
        Check(first.Complete && first.Posts.Count == 151 && first.FullScanAt.Length > 0, "initial discovery saves entire catalog without post-detail requests");
        var loaded = Catalog.Load(root, creator);
        Check(loaded.Posts.Count == 151 && loaded.ToPosts()[0].Assets().Count > 0, "catalog survives restart with attachment and link metadata");
        handler.Calls.Clear();
        var quick = await api.Discover(creator, root, false, _ => { }, CancellationToken.None);
        Check(quick.Posts.Count == 151 && handler.Calls.Count == 2, "unchanged quick refresh needs only two listing requests");
        items.Insert(0, Raw("new")); handler.Calls.Clear();
        var updated = await api.Discover(creator, root, false, _ => { }, CancellationToken.None);
        Check(updated.Posts.Count == 152 && updated.Posts.Any(p => Post.Str(p, "id") == "new"), "quick refresh merges new posts with entire cached archive");
        var fullPost = P(Raw("1", complete: true)); await Catalog.SavePost(root, fullPost);
        handler.Calls.Clear();
        var afterDetail = await api.Discover(creator, root, false, _ => { }, CancellationToken.None);
        Check(handler.Calls.Count <= 4 && afterDetail.ToPosts().First(p => p.Id == "1").Incomplete == false, "resolved full post retained without invalidating lightweight page signatures");
        items[140] = Raw("140-old-edit", title: "Changed older post");
        handler.Calls.Clear();
        var full = await api.Discover(creator, root, true, _ => { }, CancellationToken.None);
        Check(full.Posts.Any(p => Post.Str(p, "id") == "140-old-edit") && handler.Calls.Count >= 4, "full refresh revisits older pages and records edits/removals");
        var previous = File.ReadAllText(Catalog.FilePath(root, creator));
        using var cancel = new CancellationTokenSource();
        bool stopped = false;
        try { await api.Discover(creator, root, true, _ => cancel.Cancel(), cancel.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped && File.ReadAllText(Catalog.FilePath(root, creator)) == previous, "cancelled refresh preserves previous complete catalog byte-for-byte");
        var directRoot = Root("catalog-direct");
        await Catalog.SavePost(directRoot, P(Raw("direct")), true);
        var direct = Catalog.Load(directRoot, creator);
        Check(!direct.Complete && direct.DirectPostIds.Contains("direct") && direct.Posts.Count == 1, "direct post is cached without pretending full creator scan completed");
        var directAfterScan = await api.Discover(creator, directRoot, true, _ => { }, CancellationToken.None);
        Check(directAfterScan.Posts.Any(p => Post.Str(p, "id") == "direct"), "full refresh preserves explicitly saved posts absent from creator listing");
        var other = creator with { Id = "other" };
        Check(Catalog.Load(root, other).Posts.Count == 0 && Catalog.LoadAll(root).Count == 1, "catalogs stay isolated by creator and library folder");
        var post = P(Raw());
        Check(ReferenceEquals(post.Assets(), post.Assets()) && ReferenceEquals(post.Links(), post.Links()) && post.Fingerprint() == post.Fingerprint(), "post parsing results reused across UI refreshes");
    }
    static async Task CatalogLive()
    {
        using var api = new Api(); var creator = new Creator("patreon", "262481", "personalami");
        var root = Path.GetFullPath("work/live-catalog-v12"); Directory.CreateDirectory(root);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var full = await api.Discover(creator, root, true, _ => { }, CancellationToken.None);
        Console.WriteLine($"LIVE FULL: {full.Posts.Count} posts, {timer.Elapsed.TotalSeconds:0.00} seconds");
        timer.Restart(); var quick = await api.Discover(creator, root, false, _ => { }, CancellationToken.None);
        Console.WriteLine($"LIVE QUICK: {quick.Posts.Count} posts, {timer.Elapsed.TotalSeconds:0.00} seconds");
        timer.Restart(); var cached = Catalog.LoadAll(root); var posts = cached.SelectMany(c => c.ToPosts()).ToList();
        foreach (var post in posts) { post.Assets(); post.Links(); }
        Console.WriteLine($"LOCAL CACHE: {posts.Count} posts, {timer.Elapsed.TotalSeconds:0.00} seconds");
    }
    static async Task ArchiveFixtures(string folder)
    {
        foreach (var name in new[] { "7Zip.LZMA.7z", "Rar.rar", "Rar.solid.rar", "Rar5.rar", "Tar.tar.gz" })
        {
            var root = Root("fixture"); File.Copy(Path.Combine(folder, name), Path.Combine(root, name));
            var entry = new FileEntry { RelativePath = name };
            await Archives.Extract(root, entry, name, CancellationToken.None);
            Check(Archives.Present(root, entry) && entry.ExtractedFiles.Count > 0, name + " extracts real reference archive");
            if (name.EndsWith(".tar.gz")) Check(!entry.ExtractedFiles.Any(f => f.Path.EndsWith(".tar")), "TAR.GZ extracts contained media, not an intermediate TAR");
        }
        var encryptedRoot = Root("encrypted"); var encrypted = "7Zip.LZMA.Aes.7z";
        File.Copy(Path.Combine(folder, encrypted), Path.Combine(encryptedRoot, encrypted));
        var file = new FileEntry { RelativePath = encrypted }; bool failed = false;
        try { await Archives.Extract(encryptedRoot, file, encrypted, CancellationToken.None); } catch { failed = true; }
        Check(failed && !file.ExtractionComplete && File.Exists(Path.Combine(encryptedRoot, encrypted)), "encrypted archive kept intact and not marked unpacked");
    }
    static async Task NewFeatures()
    {
        var address = PostAddress.Parse("https://pawchive.pw/fanbox/user/37736420/post/12895469?x=1#files");
        Check(address.Creator.Id == "37736420" && address.Id == "12895469", "parse direct post URL with optional query and fragment");
        bool rejected = false; try { PostAddress.Parse("https://pawchive.pw/patreon/user/305523"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "direct post field rejects author-only URL");
        var directHandler = new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.EndsWith("profile") ? FakeHandler.Json(new { name = "Direct author" }) : FakeHandler.Json(Raw()) };
        using (var directApi = new Api(directHandler, TimeSpan.Zero))
        {
            var direct = await directApi.GetPost(PostAddress.Parse(creator.Url + "/post/1"), CancellationToken.None);
            Check(direct.Id == "1" && direct.Creator.Name == "Direct author" && directHandler.Calls.Count == 2, "single-post lookup does not scan creator archive");
        }
        var counter = new TransferCounter();
        using (var api = new Api(new SlowHandler(counter), TimeSpan.Zero))
        using (var api2 = new Api(new SlowHandler(counter), TimeSpan.Zero))
        {
            var root = Root("parallel-api");
            await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? api : api2).Download(new("key" + i, i + ".bin", "https://n1.pawchive.pw/data/" + i), root, i + ".bin", (_, _) => { }, CancellationToken.None)));
            Check(counter.Max == 2 && counter.Started == 6 && counter.Active == 0, "global cap of two covers entire response bodies across Api instances");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await api.Download(new("c", "c.bin", "https://n1.pawchive.pw/data/c"), root, "c.bin", (_, _) => { }, cancel.Token); } catch (OperationCanceledException) { }
            Check(counter.Active == 0, "cancelled permit wait does not leak download slots");
        }
        var postCounter = new TransferCounter();
        using (var api = new Api(new SlowHandler(postCounter), TimeSpan.Zero))
        {
            var root = Root("parallel-post"); var history = new History(); var frames = new List<MediaProgress>();
            var post = P(Raw(extra: true));
            await new Downloader(api, root, history).SavePost(post, _ => { }, f => { lock (frames) frames.Add(f); }, CancellationToken.None);
            Check(postCounter.Max == 2 && history.Posts[post.Key].Files.Count == 3, "post queue uses two workers and persists all completed files");
            Check(frames.Any(f => f.Slot == 0 && f.Received > 0 && f.Received < f.Total) && frames.Any(f => f.Slot == 1 && f.Received > 0 && f.Received < f.Total), "each active file reports intermediate byte progress");
            Check(frames.Last().Finished == 3 && frames.Last().Count == 3, "post progress reaches all-files-complete");
        }
        var archiveRoot = Root("archives");
        var zipPath = Path.Combine(archiveRoot, "sample.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var output = new StreamWriter(zip.CreateEntry("./nested/hello.txt").Open()); output.Write("hello world");
        }
        var file = new FileEntry { RelativePath = "sample.zip", Length = new FileInfo(zipPath).Length };
        await Archives.Extract(archiveRoot, file, "sample.zip", CancellationToken.None);
        Check(Archives.Present(archiveRoot, file) && File.ReadAllText(Path.Combine(archiveRoot, file.ExtractedFiles[0].Path)) == "hello world" && File.Exists(zipPath), "ZIP automatically extracts nested paths and preserves original archive");
        var firstFolder = file.ExtractedFolder;
        Check(file.ExtractedFiles.Count == 1, "extraction manifest records resulting files");
        File.Delete(Path.Combine(archiveRoot, file.ExtractedFiles[0].Path));
        Check(!Archives.Present(archiveRoot, file), "missing unpacked media triggers re-extraction");
        await Archives.Extract(archiveRoot, file, "sample.zip", CancellationToken.None);
        Check(file.ExtractedFolder != firstFolder && Archives.Present(archiveRoot, file), "re-extraction keeps existing output folder intact");
        var unsafePath = Path.Combine(archiveRoot, "unsafe.zip");
        using (var zip = ZipFile.Open(unsafePath, ZipArchiveMode.Create))
        { using var output = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); output.Write("unsafe"); }
        var unsafeFile = new FileEntry { RelativePath = "unsafe.zip" };
        rejected = false; try { await Archives.Extract(archiveRoot, unsafeFile, "unsafe.zip", CancellationToken.None); } catch (IOException) { rejected = true; }
        Check(rejected && !File.Exists(Path.Combine(archiveRoot, "escape.txt")) && !unsafeFile.ExtractionComplete, "archive traversal cannot write outside extraction directory");
        Check(!Directory.EnumerateDirectories(archiveRoot, ".extract-*").Any(), "failed extraction removes only its temporary directory");

        var zipBytes = File.ReadAllBytes(zipPath); int downloads = 0;
        var archiveHandler = new FakeHandler { Respond = _ => { downloads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) }; } };
        using (var api = new Api(archiveHandler, TimeSpan.Zero))
        {
            var root = Root("archive-pipeline"); var history = new History();
            var post = P(new { id = "archive", title = "Archive", content = "", file = new { name = "media.zip", path = "/a/b/media.zip" } });
            var downloader = new Downloader(api, root, history);
            await downloader.SavePost(post, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(history.State(root, post) == "Скачан" && history.Posts[post.Key].Files.Values.Single().ExtractionComplete, "download pipeline automatically unpacks archives and saves manifest");
            var stored = history.Posts[post.Key].Files.Values.Single(); File.Delete(Path.Combine(root, stored.ExtractedFiles[0].Path));
            Check(history.State(root, post) == "Нужна распаковка", "history flags missing extracted files");
            await downloader.SavePost(post, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(downloads == 1 && history.State(root, post) == "Скачан", "re-extract missing media without re-downloading archive");
        }
        var brokenHandler = new FakeHandler { Respond = _ => FakeHandler.File("this is not an archive") };
        using (var api = new Api(brokenHandler, TimeSpan.Zero))
        {
            var root = Root("bad-archive"); var history = new History();
            var post = P(new { id = "badarchive", title = "Bad archive", content = "", file = new { name = "bad.zip", path = "/a/b/bad.zip" } });
            await new Downloader(api, root, history).SavePost(post, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(history.Posts[post.Key].Files.Values.Single().ExtractionError.Length > 0 && !history.Posts[post.Key].Complete && File.Exists(Path.Combine(root, history.Posts[post.Key].Folder, "post.txt")), "broken archive remains saved with retryable extraction error and metadata");
        }
    }
    static async Task Live()
    {
        using var api = new Api();
        foreach (var c in new Settings().Creators)
        {
            var profile = await api.Profile(c, CancellationToken.None);
            var page = await api.Json(c.Service + "/user/" + c.Id + "?o=0", CancellationToken.None);
            if (page.ValueKind != JsonValueKind.Array || page.GetArrayLength() == 0) throw new Exception("No posts: " + c.Key);
            var post = new Post { Creator = profile, Raw = page[0].Clone() };
            var detail = await api.Detail(post, CancellationToken.None);
            if (detail.Incomplete) throw new Exception("Incomplete: " + c.Key);
            Console.WriteLine($"LIVE OK: {profile.Name}: page {page.GetArrayLength()}, post {detail.Id}, assets {detail.Assets().Count}, links {detail.Links().Count}");
        }
        // One small original only, for transport validation. Never start a bulk archive.
        var anna = new Creator("fanbox", "37736420", "Anna Anon");
        var json = await api.Json(anna.Service + "/user/" + anna.Id + "?o=0", CancellationToken.None);
        var sample = new Post { Creator = anna, Raw = json[0].Clone() };
        var asset = sample.Assets().First(a => a.Name == "cover.jpeg");
        var result = await api.Download(asset, Root("live-probe"), "original.jpeg", (_, _) => { }, CancellationToken.None);
        Console.WriteLine($"LIVE DOWNLOAD OK: original JPEG, {result.Length} bytes, SHA256 {result.Sha256}");
    }
    static async Task PermanentLive()
    {
        using var api = new Api();
        var post = await api.GetPost(PostAddress.Parse("https://pawchive.pw/patreon/user/262481/post/165545625"), CancellationToken.None);
        Console.WriteLine($"LIVE FULL-FILE PLAN: {post.Assets().Count} full files, {post.SkippedAssets().Count} entries without a full URL");
        foreach (var asset in post.Assets())
        {
            if (!Post.FullFile(new Uri(asset.Url))) throw new Exception("Thumbnail in queue");
            Console.WriteLine($"ORIGINAL: {asset.Name}, server {new Uri(asset.Url).Host}, path {new Uri(asset.Url).AbsolutePath}");
        }
        // Inspect metadata only: no archive or photo is downloaded.
    }
    static async Task FullPhotoLive()
    {
        using var api = new Api();
        var post = await api.GetPost(PostAddress.Parse("https://pawchive.pw/patreon/user/262481/post/165545625"), CancellationToken.None);
        var asset = post.Assets().First(a => a.Expiring);
        var file = await api.Download(asset, Root("full-photo-live"), "full-photo.jpg", (_, _) => { }, CancellationToken.None);
        Console.WriteLine($"LIVE FULL PHOTO OK: {file.Length} bytes from {new Uri(asset.Url).Host}; thumbnail excluded");
    }
    static async Task PartialAndTemporaryChecks()
    {
        byte[] ZipBytes()
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            { using var text = new StreamWriter(zip.CreateEntry("media/hello.txt").Open()); text.Write("hello"); }
            return memory.ToArray();
        }
        var bytes = ZipBytes(); bool unavailable = true;
        var handler = new FakeHandler { Respond = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("missing.jpg") && unavailable) return new(HttpStatusCode.NotFound);
            return path.EndsWith(".zip") ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : FakeHandler.File("image");
        }};
        using (var api = new Api(handler, TimeSpan.Zero))
        {
            var post = P(new { id = "partial", title = "Partial", content = "<a href='https://example.com/archive'>link</a>",
                file = new { name = "missing.jpg", path = "/a/missing.jpg" },
                attachments = new[] { new { name = "one.zip", path = "/b/one.zip" }, new { name = "two.zip", path = "/c/two.zip" }, new { name = "other.jpg", path = "/d/other.jpg" } } });
            var root = Root("partial-photo"); var history = new History(); var downloader = new Downloader(api, root, history);
            await downloader.SavePost(post, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            var entry = history.Posts[post.Key];
            Check(entry.Files.Count == 3 && entry.FileErrors.Count == 1 && !entry.Complete, "one missing photo does not stop remaining downloads");
            Check(entry.Files.Values.Count(f => f.ExtractionComplete) == 2, "both available archives unpack despite photo error");
            Check(post.State.StartsWith("Частично") && post.Error.Length == 0 && entry.FileErrors.Values.Single().StatusCode == 404, "post has partial state and a specific per-file HTTP error");
            Check(handler.Calls["/data/a/missing.jpg"] == 1, "permanent HTTP 404 is not retried three times");
            var folder = Path.Combine(root, entry.Folder);
            Check(File.ReadAllText(Path.Combine(folder, "post.txt")).Contains("missing.jpg") && File.ReadAllText(Path.Combine(folder, "post.txt")).Contains("example.com"), "partial post retains links, metadata and named file error report");
            var archiveFolders = entry.Files.Values.Where(f => f.ExtractionComplete).Select(f => f.ExtractedFolder).ToList();
            var callsBefore = handler.Calls.Values.Sum(); unavailable = false;
            await downloader.SavePost(post, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(handler.Calls.Values.Sum() == callsBefore + 1 && entry.Complete && entry.FileErrors.Count == 0, "retry downloads only failed photo and clears partial state");
            Check(entry.Files.Values.Where(f => f.ExtractionComplete).Select(f => f.ExtractedFolder).SequenceEqual(archiveFolders), "retry keeps successful extraction folders without unpacking again");
        }
        object TempRaw(string suffix, bool expired = false) => new
        {
            id = "temp", title = "Temporary photos", content = "",
            file = new { name = "one.jpg", path = "/aa/one.jpg", preview_only = true, temp_url = "https://t2.pawchive.pw/f/one-" + suffix + ".jpg", temp_expires = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1).ToString("O") },
            attachments = new object[] {
                new { name = "one.jpg", path = "/aa/one.jpg", preview_only = true },
                new { name = "two.jpg", path = "/bb/two.jpg", preview_only = true, temp_url = "https://t3.pawchive.pw/f/two-" + suffix + ".jpg", temp_expires = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1).ToString("O") }
            }
        };
        var cached = P(TempRaw("old")); var fresh = P(TempRaw("new"));
        Check(cached.Assets().Count == 2 && cached.SkippedAssets().Count == 0 && cached.Assets().All(a => a.Expiring), "preview-only stored descriptors still download available full temp_url files");
        Check(cached.Fingerprint() == fresh.Fingerprint(), "temporary URL rotation does not mark downloaded post as edited");
        var tempHandler = new FakeHandler { Respond = r =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("/post/")) return FakeHandler.Json(TempRaw("new"));
            return r.RequestUri.AbsolutePath.Contains("old") ? new(HttpStatusCode.Forbidden) : FakeHandler.File("photo");
        }};
        using (var api = new Api(tempHandler, TimeSpan.Zero))
        {
            var root = Root("temp-refresh"); var history = new History();
            await new Downloader(api, root, history).SavePost(cached, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(history.Posts[cached.Key].Complete && history.Posts[cached.Key].Files.Count == 2 && history.Posts[cached.Key].SkippedFiles.Count == 0, "full photos download after rejected signed URLs are refreshed");
            Check(tempHandler.Calls.Where(p => p.Key.Contains("/post/")).Sum(p => p.Value) == 1, "two expiring photos share one refresh request");
        }
        var expiryHandler = new FakeHandler { Respond = r => r.RequestUri!.AbsolutePath.Contains("/post/") ? FakeHandler.Json(TempRaw("new")) : FakeHandler.File("photo") };
        using (var api = new Api(expiryHandler, TimeSpan.Zero))
        {
            var root = Root("temp-expired"); var expired = P(TempRaw("expired", true));
            await new Downloader(api, root, new History()).SavePost(expired, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(!expiryHandler.Calls.Keys.Any(k => k.Contains("expired")) && expiryHandler.Calls.Where(p => p.Key.Contains("/post/")).Sum(p => p.Value) == 1, "expired full-file links refresh before download without sending old URLs");
        }
        var normal = P(new { id = "normal", file = new { name = "x.jpg", path = "/a/x.jpg", server = "https://n4.pawchive.pw", temp_url = "https://t1.pawchive.pw/f/temp" } });
        Check(normal.Assets().Single().Url == "https://n4.pawchive.pw/data/a/x.jpg", "permanent CDN supplied by API is used instead of temp_url");
        var dedup = P(new { id = "dedup", file = new { name = "x.jpg", path = "/a/x.jpg", preview_only = true }, attachments = new[] { new { name = "x.jpg", path = "/a/x.jpg", server = "https://n3.pawchive.pw" } } });
        Check(dedup.Assets().Count == 1 && dedup.SkippedAssets().Count == 0 && dedup.Assets()[0].Url.StartsWith("https://n3."), "a permanent duplicate overrides a preview descriptor");
        var fallback = P(new { id = "fallback", file = new { name = "x.jpg", path = "/a/x.jpg" }, attachments = new[] { new { name = "x.jpg", path = "/a/x.jpg", server = "https://n2.pawchive.pw" } } });
        Check(fallback.Assets().Single().Url == "https://n2.pawchive.pw/data/a/x.jpg", "explicit attachment server overrides legacy main-file host fallback");
        var inline = P(new { id = "inline-preview", content = "<img src='/data/aa/one.jpg'>", file = new { name = "one.jpg", path = "/aa/one.jpg", preview_only = true } });
        Check(inline.Assets().Count == 0, "inline data link cannot reintroduce a skipped preview");
        var policyHandler = new FakeHandler { Respond = _ => throw new Exception("No request expected") };
        using (var api = new Api(policyHandler, TimeSpan.Zero))
        {
            var root = Root("blocked-thumbnails"); int rejected = 0;
            foreach (var url in new[] { "https://img.pawchive.pw/thumbnail/data/file", "https://n1.pawchive.pw/thumbnail/data/file", "https://t4.pawchive.pw/thumbnail/data/file" })
            { try { await api.Download(new("key", "file.jpg", url), root, "file.jpg", (_, _) => { }, CancellationToken.None); } catch (IOException) { rejected++; } }
            Check(rejected == 3 && policyHandler.Calls.Count == 0, "thumbnail resources are blocked on every CDN without network requests");
        }
        var lazy = P(new { id = "lazy", content = "<a href='https://t4.pawchive.pw/f/full.jpg?e=9999999999&amp;s=test'><img src='https://img.pawchive.pw/thumbnail/data/preview.jpg' data-src='https://img.pawchive.pw/thumbnail/data/preview.jpg' loading='lazy'></a>" });
        Check(lazy.Assets().Count == 1 && lazy.Assets()[0].Url.StartsWith("https://t4.pawchive.pw/f/full.jpg") && lazy.Assets()[0].Expiring, "anchor full file on t4 is queued while lazy image thumbnail is excluded");
        var fullLazy = P(new { id = "full-lazy", content = "<img loading='lazy' src='https://n3.pawchive.pw/data/full.jpg'>" });
        Check(fullLazy.Assets().Count == 1, "loading=lazy alone does not exclude a full-size file");
        var preview = P(new { id = "preview-alone", file = new { name = "preview.jpg", path = "/aa/preview.jpg", preview_only = true } });
        Check(preview.Assets().Count == 0 && preview.SkippedAssets().Count == 1, "stored thumbnail without a full URL is explicitly skipped");
        var relayHandler = new FakeHandler { Respond = _ => FakeHandler.File("full-photo") };
        using (var api = new Api(relayHandler, TimeSpan.Zero))
        {
            var root = Root("relay-full");
            foreach (var host in new[] { "t1", "t4" }) await api.Download(new(host, host + ".jpg", "https://" + host + ".pawchive.pw/f/full.jpg"), root, host + ".jpg", (_, _) => { }, CancellationToken.None);
            Check(relayHandler.Calls.Values.Sum() == 2, "full files on both t1 and t4 pass the transport filter");
        }
        var redirectHandler = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://img.pawchive.pw/thumbnail/data/fallback.jpg"),
            Content = new ByteArrayContent([1, 2, 3])
        }};
        using (var api = new Api(redirectHandler, TimeSpan.Zero))
        {
            var root = Root("thumbnail-redirect"); bool rejected = false;
            try { await api.Download(new("redirect", "full.jpg", "https://t4.pawchive.pw/f/full.jpg"), root, "full.jpg", (_, _) => { }, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(Path.Combine(root, "full.jpg")), "redirect from a full-file URL to a thumbnail cannot save the thumbnail as original");
        }
        var hosts = new List<string>();
        var movedHandler = new FakeHandler { Respond = r =>
        {
            hosts.Add(r.RequestUri!.Host);
            if (r.RequestUri.AbsolutePath.Contains("/post/")) return FakeHandler.Json(new { id = "normal", title = "updated server", content = "", file = new { name = "x.jpg", path = "/a/x.jpg", server = "https://n3.pawchive.pw" } });
            return r.RequestUri.Host == "n4.pawchive.pw" ? new(HttpStatusCode.NotFound) : FakeHandler.File("original");
        }};
        using (var api = new Api(movedHandler, TimeSpan.Zero))
        {
            var root = Root("moved-server"); var history = new History();
            await new Downloader(api, root, history).SavePost(normal, _ => { }, (MediaProgress _) => { }, CancellationToken.None);
            Check(history.Posts[normal.Key].Complete && hosts.Contains("n3.pawchive.pw") && !hosts.Any(h => h.StartsWith("t1.")), "404 refresh uses new permanent server from API without guessing or using temp_url");
        }
    }
}
