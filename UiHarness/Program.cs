using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Pawchive;

class SlowMedia(byte[] data) : MemoryStream(data)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(350, cancellationToken); return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 32768)], cancellationToken); }
}
class DemoServer : HttpMessageHandler
{
    public static readonly byte[] Zip = MakeZip();
    static byte[] MakeZip()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        { using var text = new StreamWriter(zip.CreateEntry("folder/readme.txt").Open()); text.Write("Archive extraction verified."); }
        return memory.ToArray();
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        if (path.EndsWith("missing.jpg")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if (path.EndsWith("profile")) response.Content = new StringContent("{\"name\":\"Проверка интерфейса\"}", System.Text.Encoding.UTF8, "application/json");
        else if (path.Contains("/post/")) response.Content = new StringContent(JsonSerializer.Serialize(new
        {
            id = "777", title = "Полная версия t4 без lazy-миниатюры", published = "2026-11-11T10:00:00", has_full = true, detail_fetched = true,
            content = "<p><a href='https://example.com/file#secret'>Тестовая ссылка</a><img loading='lazy' src='https://img.pawchive.pw/thumbnail/data/preview.jpg'></p>",
            file = new { name = "photo.jpg", path = "/demo/photo.jpg", preview_only = true, temp_url = "https://t4.pawchive.pw/f/full-photo.jpg" },
            attachments = new[] { new { name = "media-2.bin", path = "/demo/2.bin" }, new { name = "archive.zip", path = "/demo/3.zip" } }
        }), System.Text.Encoding.UTF8, "application/json");
        else response.Content = new StreamContent(new SlowMedia(path.EndsWith(".zip") ? Zip : new byte[512 * 1024]));
        return Task.FromResult(response);
    }
}
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var path = Path.Combine(AppContext.BaseDirectory, "ui-settings-1.9.json");
        var root = Path.Combine(AppContext.BaseDirectory, "Downloads-1.9");
        var creators = new List<Creator> { new("patreon", "demo-a", "Автор А"), new("fanbox", "demo-b", "Автор Б"), new("patreon", "demo-c", "Автор В"), new("fanbox", "demo-d", "Автор Г", false), new("patreon", "demo-e", "Автор Д", false) };
        foreach (var creator in creators)
        {
            var catalog = new Catalog { Creator = creator, Complete = true, CheckedAt = DateTimeOffset.Now.ToString("O") };
            for (int i = 0; i < (creator.Id == "demo-a" ? 225 : 4); i++)
            {
                string[] titles = ["Day 28. Mermaid", "Day 27. Lich", "Day 26. Swamp Witch", "Day 25. Celestial Being", "Day 24. Rune Master", "Day 23. Phoenix Keeper", "Day 22. Arcane Scholar", "Day 21. Shadow Priest", "Day 20. Dragonborn"];
                catalog.Posts.Add(JsonSerializer.SerializeToElement(new { id = (1000 + i).ToString(), title = titles[i % titles.Length], published = DateTime.Now.AddDays(-i).ToString("s"), has_full = true, detail_fetched = true,
                    content = "<p>Описание поста. <a href='https://example.com/collection'>Внешняя ссылка</a></p>",
                    file = new { name = "illustration.jpg", path = $"/demo/{creator.Id}/{i}.jpg" }, attachments = new[] { new { name = "source-files.zip", path = $"/demo/{creator.Id}/{i}.zip" } } }));
            }
            catalog.Save(root);
        }
        if (!File.Exists(path)) Storage.WriteJson(path, new Settings { Root = root, Creators = creators, Theme = "dark", Language = "en" });
        var form = new MainForm(new Api(new DemoServer(), TimeSpan.Zero), path);
        Application.Run(form);
    }
}
