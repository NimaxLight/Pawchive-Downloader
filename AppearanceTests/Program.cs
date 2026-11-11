using System.Reflection;
using System.Text.Json;
using Pawchive;
using System.Net;
using System.Runtime.InteropServices;

class FlowHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpContent?>? MediaContent;
    public Action<HttpRequestMessage>? OnRequest;
    public object? FullPost;
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> Calls = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        OnRequest?.Invoke(request);
        string path = request.RequestUri!.AbsolutePath; Calls.AddOrUpdate(path, 1, (_, count) => count + 1);
        var content = path.Contains("/post/") ? new StringContent(JsonSerializer.Serialize(FullPost), System.Text.Encoding.UTF8, "application/json") : new StringContent("original file content", System.Text.Encoding.UTF8, "application/octet-stream");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = MediaContent?.Invoke(request) ?? content });
    }
}

sealed class SkippableStream(Action started) : MemoryStream(new byte[1048576])
{
    bool sent;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (!sent) { sent = true; int count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], token); started(); return count; }
        await Task.Delay(Timeout.Infinite, token); return 0;
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    static void ClickCreatorCell(DataGridView grid, string key, string column)
    {
        var row = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == key);
        var rect = grid.GetCellDisplayRectangle(grid.Columns[column]!.Index, row.Index, false);
        int x = rect.Left + rect.Width / 2, y = rect.Top + 18;
        var point = (IntPtr)((y << 16) | (x & 65535));
        SendMessage(grid.Handle, 0x201, (IntPtr)1, point); SendMessage(grid.Handle, 0x202, IntPtr.Zero, point);
        Application.DoEvents();
    }
    static int assertions;
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    static object? Call(object obj, string name, params object[] values) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, values);
    static IEnumerable<Control> All(Control root) { yield return root; foreach (Control child in root.Controls) foreach (var descendant in All(child)) yield return descendant; }
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        foreach (bool dark in new[] { false, true })
        foreach (int red in new[] { 0, 80, 160, 255 }) foreach (int green in new[] { 0, 80, 160, 255 }) foreach (int blue in new[] { 0, 80, 160, 255 })
        {
            var p = AppPalette.Create(dark, Color.FromArgb(red, green, blue));
            Check(AppPalette.Contrast(p.AccentText, p.Accent) >= 4.5, "Primary button text contrast");
            Check(AppPalette.Contrast(p.AccentInk, p.Surface) >= 4.5, "Accent text contrast");
            Check(AppPalette.Contrast(AppPalette.Readable(p.Text, p.Selection), p.Selection) >= 4.5, "Selected row text contrast");
        }
        var legacy = JsonSerializer.Deserialize<Settings>("{\"Root\":\"C:\\\\Downloads\",\"Creators\":[]}")!;
        Check(legacy.Theme == "system" && legacy.AccentMode == "system", "Old settings must follow system by default");
        string temp = Path.Combine(AppContext.BaseDirectory, "test-library-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        string settingsPath = Path.Combine(temp, "settings.json"); Storage.WriteJson(settingsPath, new Settings { Root = temp, Creators = [] });
        var flowHandler = new FlowHandler();
        using var form = new MainForm(new Api(flowHandler, TimeSpan.Zero), settingsPath);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(200);
                var settings = Field<Settings>(form, "settings");
                var posts = Field<List<Post>>(form, "posts"); var creator = new Creator("patreon", "123", "Layout author");
                settings.Creators = [creator]; Call(form, "FillAuthors");
                for (int i = 0; i < 215; i++) posts.Add(new Post { Creator = creator, Raw = JsonSerializer.SerializeToElement(new { id = i.ToString(), title = "Post " + i, published = "2026-11-11", content = "", attachments = Array.Empty<object>() }) });
                Call(form, "FillPosts");
                var table = Field<DataGridView>(form, "table");
                Check(!table.AllowUserToResizeRows && !Field<DataGridView>(form, "authors").AllowUserToResizeRows, "Row resizing is disabled");
                var addButton = Field<ModernButton>(form, "add");
                Check(addButton.Width == addButton.Height, "Add author button is square after layout");
                var filter = Field<ModernCombo>(form, "filter");
                Check(table.PointToScreen(Point.Empty).Y - filter.PointToScreen(new Point(0, filter.Height)).Y >= 10, "Filter has space above the table");
                Check(form.Icon is not null && All(form).OfType<BrandView>().Any(), "Application and header use the supplied brand asset");
                Check(table.Rows.Count == 200, "Pagination first page");
                Field<ModernButton>(form, "nextPage").PerformClick(); Check(table.Rows.Count == 15, "Pagination second page");
                Field<ModernButton>(form, "previousPage").PerformClick();
                var originalPosts = posts.ToArray();
                var secondCreator = new Creator("fanbox", "456", "Second author");
                var standaloneCreator = new Creator("patreon", "789", "Standalone author");
                Post Sample(Creator author, string id, string date, string state = "Не скачан") => new() { Creator = author, Raw = JsonSerializer.SerializeToElement(new { id, title = id, published = date }), State = state };
                var older = Sample(creator, "older", "2026-01-01", "Скачан");
                var newer = Sample(creator, "newer", "2026-02-01");
                var other = Sample(secondCreator, "other", "2026-03-01", "Скачан");
                var direct = Sample(standaloneCreator, "direct", "2026-04-01");
                var directPosts = Field<HashSet<string>>(form, "directPosts");
                directPosts.Add(newer.Key); directPosts.Add(direct.Key);
                settings.Creators = [creator, secondCreator];
                posts.Clear(); posts.AddRange([other, older, direct, newer]);
                Call(form, "FillAuthors"); Call(form, "FillPosts");
                Post[] Displayed() => table.Rows.Cast<DataGridViewRow>().Select(r => (Post)r.Tag!).ToArray();
                Check(Displayed().SequenceEqual([newer, older, other, direct]), "Creators follow sidebar order with newest posts first within each group");
                var authorChoice = Field<ModernCombo>(form, "authorFilter");
                Check(authorChoice.Items.Count == 3 && authorChoice.SelectedIndex == 0, "Multiple checked creators offer All and one entry per creator");
                Check(authorChoice.Top == filter.Top && authorChoice.Height == filter.Height && authorChoice.Left >= filter.Right, "Creator filter aligns beside status filter");
                authorChoice.SelectedIndex = 2;
                Check(Displayed().SequenceEqual([other]) && settings.Creators.All(c => c.Selected), "Creator view filter leaves download selections unchanged");
                filter.SelectedIndex = 1;
                Check(table.Rows.Count == 0, "Creator and status filters combine");
                filter.SelectedIndex = 0;
                settings.Language = "en"; Call(form, "ApplyUiLanguage");
                Check(authorChoice.Items[0]?.ToString() == "All selected creators" && authorChoice.SelectedIndex == 2 && Displayed().SequenceEqual([other]), "Language switching preserves creator identity and localized All entry");
                settings.Language = "ru"; Call(form, "ApplyUiLanguage");
                authorChoice.SelectedIndex = 0;
                var authorGrid = Field<DataGridView>(form, "authors");
                authorGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == creator.Key).Cells[0].Value = false;
                Check(Displayed().SequenceEqual([other]) && authorChoice.Items.Count == 1, "Single remaining creator has exactly one view choice");
                Check(Field<Label>(form, "libraryCaption").Text.StartsWith("2 ") && !Field<ModernButton>(form, "nextPage").Enabled, "Counts and pagination reflect selected creators");
                filter.SelectedIndex = 2;
                Check(Displayed().SequenceEqual([other]), "Status filter applies to selected creators only");
                authorGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == secondCreator.Key).Cells[0].Value = false;
                Check(table.Rows.Count == 0, "Unchecking last matching creator empties filtered table");
                filter.SelectedIndex = 0;
                Check(Displayed().SequenceEqual([direct]), "Standalone URL post remains available without a listed creator");
                Check(authorChoice.Items.Count == 0 && authorChoice.SelectedIndex == -1 && !authorChoice.Enabled, "No checked creators leaves an empty disabled creator filter");
                authorGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == creator.Key).Cells[0].Value = true;
                Check(Displayed().SequenceEqual([newer, older]), "Rechecking creator restores cached posts without a network scan");
                settings.Creators = [creator]; posts.Clear(); posts.AddRange(originalPosts); directPosts.Clear();
                Call(form, "FillAuthors"); Call(form, "FillPosts");
                Field<ModernButton>(form, "nextPage").PerformClick();
                authorGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == creator.Key).Cells[0].Value = false;
                Check(table.Rows.Count == 0 && Field<int>(form, "currentPage") == 0 && !Field<ModernButton>(form, "openPostFolder").Enabled, "Unchecking from a later page resets pagination and folder action");
                authorGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == creator.Key).Cells[0].Value = true;
                Check(table.Rows.Count == 200 && Field<int>(form, "currentPage") == 0 && posts.Count == 215, "Rechecking restores first page and keeps full catalog");
                foreach (string theme in new[] { "dark", "light" })
                {
                    settings.Theme = theme; settings.AccentMode = "custom"; settings.AccentColor = "#F3D976"; Call(form, "ApplyAppearance");
                    var p = Field<AppPalette>(form, "palette");
                    Check(p.Dark == (theme == "dark"), "Manual theme override"); Check(p.Accent.ToArgb() == ColorTranslator.FromHtml("#F3D976").ToArgb(), "Manual accent override");
                    foreach (DataGridViewRow row in table.Rows)
                    {
                        var inherited = row.Cells["Title"].InheritedStyle;
                        Check(AppPalette.Contrast(inherited.ForeColor, inherited.BackColor) >= 4.5, "Data row contrast");
                        Check(AppPalette.Contrast(inherited.SelectionForeColor, inherited.SelectionBackColor) >= 4.5, "Selected data row contrast");
                    }
                    foreach (var size in new[] { new Size(1080, 740), new Size(1320, 900), new Size(1640, 1000) })
                    {
                        form.Size = size; form.PerformLayout(); Application.DoEvents();
                        foreach (var button in All(form).OfType<ModernButton>().Where(c => c.Visible))
                        {
                            Check(button.Width > 0 && button.Height >= 30, "Visible buttons must fit");
                            var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                            Check(form.ClientRectangle.Contains(bounds), "Button outside main client area: " + button.Text);
                        }
                    }
                    var notify = typeof(MainForm).GetMethod("MediaUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    notify.Invoke(form, [posts[0], 1, 215, new MediaProgress(0, "very-long-file-name-source-archive.zip", 524288, 1048576, 0, 2, "Скачивание")]);
                    Check(Field<IconView[]>(form, "mediaIcons")[0].Glyph == AppIcon.Archive, "Archive icon");
                    Check(Field<Label[]>(form, "mediaDetails")[0].Text.Contains("50%"), "Progress independent of long filename");
                    notify.Invoke(form, [posts[0], 1, 215, new MediaProgress(1, "movie.mp4", 524288, 1048576, 0, 2, "Скачивание")]);
                    Check(Field<IconView[]>(form, "mediaIcons")[1].Glyph == AppIcon.Video, "Video icon");
                }
                var oldRoot = settings.Root;
                using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += (_, _) =>
                {
                    var dialog = form.OwnedForms.FirstOrDefault(); if (dialog is null) return;
                    timer.Stop();
                    var combos = All(dialog).OfType<ModernCombo>().Where(c => c.Tag as string != "language-options").ToArray(); combos[0].SelectedIndex = 1; combos[1].SelectedIndex = 0;
                    var colorButton = All(dialog).OfType<ModernButton>().First(b => b.Text == "Выбрать цвет…");
                    var folderButton = All(dialog).OfType<ModernButton>().First(b => b.Text == "Изменить…");
                    Check(colorButton.PointToScreen(Point.Empty).Y == combos[1].PointToScreen(Point.Empty).Y && colorButton.Height == combos[1].Height, "Color picker and field have equal vertical position and height");
                    Check(folderButton.PointToScreen(Point.Empty).X == colorButton.PointToScreen(Point.Empty).X && folderButton.Width == colorButton.Width, "Settings action buttons align horizontally");
                    var pathField = All(dialog).OfType<TextBox>().Single();
                    Check(pathField.Parent!.PointToScreen(Point.Empty).Y == folderButton.PointToScreen(Point.Empty).Y && pathField.Parent.Height == folderButton.Height, "Folder field frame matches button height and position");
                    foreach (var check in All(dialog).OfType<CheckBox>()) check.Checked = true;
                    All(dialog).OfType<ModernButton>().First(b => b.Text == "Сохранить").PerformClick();
                };
                timer.Start(); await (Task)Call(form, "ShowAppearance")!;
                Check(settings.Theme == "light" && settings.AccentMode == "system", "Settings dialog applies choices");
                Check(JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))!.Theme == "light", "Settings persisted");
                Check(settings.Root == oldRoot, "Appearance does not change library");
                Check(settings.DownloadPreviews && settings.RecheckDownloaded, "Download options applied and persisted");
                using var addTimer = new System.Windows.Forms.Timer { Interval = 100 };
                int addStage = 0;
                addTimer.Tick += (_, _) =>
                {
                    var dialog = form.OwnedForms.FirstOrDefault(); if (dialog is null) return;
                    var input = All(dialog).OfType<TextBox>().Single();
                    Check(input.Parent is TextFieldFrame && input.BorderStyle == BorderStyle.None, "URL input uses themed frame without native border");
                    var accept = All(dialog).OfType<ModernButton>().First(b => b.Text == "Добавить");
                    if (addStage++ == 0) { input.Text = "https://example.com/wrong"; accept.PerformClick(); Check(dialog.DialogResult != DialogResult.OK, "Invalid author rejected inside dialog"); }
                    else { addTimer.Stop(); input.Text = "https://pawchive.pw/patreon/user/555"; accept.PerformClick(); }
                };
                addTimer.Start(); Call(form, "ShowAddAuthor");
                Check(settings.Creators.Any(c => c.Id == "555"), "Author added through new dialog");
                using var cancelTimer = new System.Windows.Forms.Timer { Interval = 100 };
                cancelTimer.Tick += (_, _) => { var dialog = form.OwnedForms.FirstOrDefault(); if (dialog is null) return; cancelTimer.Stop(); All(dialog).OfType<ModernCombo>().First(c => c.Tag as string != "language-options").SelectedIndex = 2; dialog.DialogResult = DialogResult.Cancel; };
                cancelTimer.Start(); await (Task)Call(form, "ShowAppearance")!;
                Check(settings.Theme == "light", "Cancel leaves appearance unchanged");
                Field<ModernCombo>(form, "filter").SelectedIndex = 2; Check(table.Rows.Count == 0, "Downloaded filter");
                Field<ModernCombo>(form, "filter").SelectedIndex = 0; Check(table.Rows.Count == 200, "All-posts filter");
                var history = Field<History>(form, "history");
                string postFolder = Path.Combine(temp, "partial-post");
                history.Posts[posts[0].Key] = new PostEntry { Folder = "partial-post", Complete = false };
                table.CurrentCell = table.Rows[0].Cells[0]; Call(form, "UpdatePostFolder");
                Check(!Field<ModernButton>(form, "openPostFolder").Enabled, "Missing post directory disables folder button");
                Directory.CreateDirectory(postFolder); File.WriteAllText(Path.Combine(postFolder, "file.jpg"), "saved media");
                Call(form, "UpdatePostFolder");
                Check(Field<ModernButton>(form, "openPostFolder").Enabled && (string?)Call(form, "SelectedPostFolder") == postFolder, "Partially downloaded post exposes its own existing directory");
                settings.Creators = Enumerable.Range(1, 40).Select(i => new Creator("patreon", i.ToString(), "Author " + i)).ToList();
                Call(form, "FillAuthors"); Application.DoEvents();
                var authors = Field<DataGridView>(form, "authors");
                var scrollbar = All(authors.Parent!.Parent!).OfType<ThemeScrollBar>().Single();
                Check(scrollbar.Visible && scrollbar.Maximum > 0, "Large author list uses themed scrollbar");
                scrollbar.Value = scrollbar.Maximum;
                Check(authors.FirstDisplayedScrollingRowIndex == scrollbar.Value && scrollbar.Value > 0, "Themed scrollbar reaches end of author list");
                settings.Creators = [creator]; settings.DownloadPreviews = false; settings.RecheckDownloaded = false;
                Field<HashSet<string>>(form, "scanned").Add(creator.Key);
                var oldPost = new Post { Creator = creator, Raw = JsonSerializer.SerializeToElement(new { id = "flow", title = "Previously downloaded", has_full = true, detail_fetched = true, file = new { name = "original.jpg", path = "/flow/original.jpg", server = "https://n1.pawchive.pw" }, attachments = new[] { new { name = "later.jpg", path = "/flow/later.jpg", preview_only = true } } }) };
                posts.Clear(); posts.Add(oldPost);
                await new Downloader(Field<Api>(form, "api"), settings.Root, history).SavePost(oldPost, _ => { }, (long received, long? total) => { }, CancellationToken.None);
                Call(form, "FillPosts");
                flowHandler.FullPost = new { id = "flow", title = "Now available", has_full = true, detail_fetched = true, file = new { name = "original.jpg", path = "/flow/original.jpg", server = "https://n1.pawchive.pw" }, attachments = new[] { new { name = "later.jpg", path = "/flow/later.jpg", server = "https://n2.pawchive.pw" } } };
                await (Task)Call(form, "DownloadPosts", CancellationToken.None)!;
                Check(!flowHandler.Calls.Keys.Any(p => p.Contains("/post/")), "Completed posts skipped while repeat setting is disabled");
                settings.RecheckDownloaded = true;
                await (Task)Call(form, "DownloadPosts", CancellationToken.None)!;
                Check(flowHandler.Calls.Values.Sum() == 3 && flowHandler.Calls["/data/flow/original.jpg"] == 1 && flowHandler.Calls["/data/flow/later.jpg"] == 1, "Repeat setting refreshes completed post and downloads only newly available file");
                Check(history.Posts[oldPost.Key].Complete && history.Posts[oldPost.Key].Files.Count == 2 && table.Rows[0].Cells["Files"].Value?.ToString() == "2", "Rechecked post and visible file count are updated");
                string stateBefore = posts[0].State; string titleBefore = posts[0].Title;
                settings.Language = "en"; Call(form, "ApplyUiLanguage");
                Check(Field<ModernButton>(form, "appearance").Text == "Settings" && table.Columns["Title"]!.HeaderText == "Post", "English main buttons and table headers");
                Check(Field<ModernCombo>(form, "filter").Items[0]?.ToString() == "All posts", "English filter options");
                Check(table.Rows[0].Cells["State"].Value?.ToString() == "Downloaded" && posts[0].State == stateBefore && posts[0].Title == titleBefore, "Language changes presentation without changing internal state or titles");
                Check(Locale.T("Пост 2/10 • Файлов скачано: 1/3 • Автор — Ошибка") == "Post 2/10 • Files downloaded: 1/3 • Автор — Ошибка", "Dynamic progress localizes labels and preserves creator/post names");
                Check(Locale.T("Скачивание • 50% • 0,5 / 1,0 МБ") == "Downloading • 50% • 0,5 / 1,0 MB", "English transfer details");
                Check(Locale.Error("image.jpg: Сервер вернул HTML-страницу вместо файла.") == "image.jpg: The server returned an HTML page instead of a file.", "Stored file errors displayed in English");
                Field<DateTime[]>(form, "lastMediaUpdates")[0] = DateTime.MinValue;
                Call(form, "MediaUpdate", posts[0], 1, 2, new MediaProgress(0, "Ошибка", 1024, 2048, 0, 1, "Скачивание"));
                Check(Field<Label[]>(form, "mediaLabels")[0].Text == "Ошибка" && Field<Label[]>(form, "mediaDetails")[0].Text.StartsWith("Downloading"), "Filenames remain opaque while transfer stage is translated");
                using var languageTimer = new System.Windows.Forms.Timer { Interval = 100 };
                languageTimer.Tick += (_, _) =>
                {
                    var dialog = form.OwnedForms.FirstOrDefault(); if (dialog is null) return;
                    languageTimer.Stop();
                    Check(dialog.Text == "Settings" && All(dialog).OfType<CheckBox>().Any(c => c.Text == "Download image previews"), "English settings dialog and download options");
                    var languageChoice = All(dialog).OfType<ModernCombo>().Single(c => c.Tag as string == "language-options");
                    Check(languageChoice.SelectedIndex == 1 && languageChoice.Items[0]?.ToString() == "Русский", "Language picker keeps native language names");
                    Check(All(dialog).OfType<ModernButton>().Where(c => c.Visible).All(c => dialog.ClientRectangle.Contains(dialog.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)))), "Expanded settings dialog fits its buttons");
                    All(dialog).OfType<ModernButton>().Single(c => c.Text == "Save").PerformClick();
                };
                languageTimer.Start(); await (Task)Call(form, "ShowAppearance")!;
                Check(JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))!.Language == "en", "Language choice is persisted");
                settings.Language = "ru"; Call(form, "ApplyUiLanguage");
                Check(Field<ModernButton>(form, "appearance").Text == "Настройки" && table.Columns["Title"]!.HeaderText == "Пост" && Field<ModernCombo>(form, "filter").Items[0]?.ToString() == "Все посты", "Switching back restores Russian controls");
                Check(posts[0].State == "Файлы: 0/1" && history.Posts[posts[0].Key].Complete, "Language round-trip preserves progress state and download history");
                settings.Creators = [creator, secondCreator]; settings.RecheckDownloaded = false;
                var queueA = Sample(creator, "queueA", "2026-01-01"); var queueB = Sample(secondCreator, "queueB", "2026-02-01");
                posts.Clear(); posts.AddRange([queueA, queueB]);
                Field<HashSet<string>>(form, "scanned").Add(secondCreator.Key);
                Call(form, "FillAuthors"); Call(form, "FillPosts"); authorChoice.SelectedIndex = 1;
                await (Task)Call(form, "DownloadPosts", CancellationToken.None)!;
                Check(history.Posts[queueA.Key].Complete && history.Posts[queueB.Key].Complete && Displayed().SequenceEqual([queueA]), "Download processes both checked creators while table displays only one");
                var inactiveCreator = new Creator("fanbox", "900", "Inactive author", false);
                settings.Creators = [creator, inactiveCreator, secondCreator];
                posts.Clear(); posts.AddRange([queueA, queueB]);
                Call(form, "FillAuthors"); Call(form, "FillPosts"); authorChoice.SelectedIndex = 0;
                var sections = authorGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is not Creator).ToArray();
                Check(sections.Length == 2 && sections.All(r => r.ReadOnly), "Creator list has two noneditable section headings");
                ClickCreatorCell(authorGrid, creator.Key, "Selected");
                Check(!settings.Creators.Single(c => c.Key == creator.Key).Selected, "Actual mouse click on drawn top-aligned checkbox deactivates a creator");
                ClickCreatorCell(authorGrid, creator.Key, "Selected");
                Check(settings.Creators.Single(c => c.Key == creator.Key).Selected, "Actual mouse click reactivates a creator");
                Call(form, "MoveCreator", creator.Key, -1);
                ClickCreatorCell(authorGrid, creator.Key, "Author");
                Check(authorGrid.SelectedCells.Count == 0 && authorGrid.SelectedRows.Count == 0, "Mouse interaction cannot select a creator card");
                Check((bool)typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(authorGrid)!, "Creator grid uses double buffering during drag operations");
                Check(authorGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is Creator).Select(r => ((Creator)r.Tag!).Key).SequenceEqual([creator.Key, secondCreator.Key, inactiveCreator.Key]), "Inactive creators are displayed below active creators regardless of legacy setting order");
                Call(form, "MoveCreator", secondCreator.Key, -1);
                Check(settings.Creators.Select(c => c.Key).SequenceEqual([secondCreator.Key, creator.Key, inactiveCreator.Key]) && Displayed().SequenceEqual([queueB, queueA]), "Moving a creator immediately reorders sidebar, persisted queue, and post groups");
                var restored = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))!;
                Check(restored.Creators.Select(c => c.Key).SequenceEqual(settings.Creators.Select(c => c.Key)), "Queue order survives settings serialization");
                Call(form, "SetCreatorSelected", inactiveCreator.Key, true); Application.DoEvents();
                Check(settings.Creators.Where(c => c.Selected).Last().Key == inactiveCreator.Key, "Activation appends creator to the active queue");
                Call(form, "SetCreatorSelected", creator.Key, false); Application.DoEvents();
                Check(!settings.Creators.Last().Selected && settings.Creators.Last().Key == creator.Key && Displayed().SequenceEqual([queueB]), "Deactivation moves creator into inactive section and removes their posts");
                Call(form, "SetCreatorSelected", creator.Key, true); Application.DoEvents();
                Check(settings.Creators.Where(c => c.Selected).Last().Key == creator.Key, "Reactivation places creator at end rather than restoring an obsolete priority");
                using var lockedQueue = new CancellationTokenSource();
                typeof(MainForm).GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, lockedQueue);
                var lockedOrder = settings.Creators.ToArray(); Call(form, "MoveCreator", creator.Key, -1); Call(form, "SetCreatorSelected", creator.Key, false);
                Check(settings.Creators.SequenceEqual(lockedOrder), "Queue changes are rejected while an operation is active");
                typeof(MainForm).GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, null);
                Call(form, "SetCreatorSelected", inactiveCreator.Key, false); Application.DoEvents();
                using var managerTimer = new System.Windows.Forms.Timer { Interval = 100 };
                managerTimer.Tick += (_, _) =>
                {
                    var dialog = Field<Panel?>(form, "authorManager"); if (dialog is null) return; managerTimer.Stop();
                    try
                    {
                        var detailed = Field<DataGridView>(form, "expandedAuthors");
                        Check(form.OwnedForms.Length == 0 && dialog.Parent?.Parent == form, "Creator manager is embedded inside main window rather than a movable system window");
                        ClickCreatorCell(detailed, inactiveCreator.Key, "Selected");
                        Check(settings.Creators.Single(c => c.Key == inactiveCreator.Key).Selected, "Embedded manager checkbox activates a creator by mouse");
                        ClickCreatorCell(detailed, inactiveCreator.Key, "Selected");
                        Check(!settings.Creators.Single(c => c.Key == inactiveCreator.Key).Selected, "Embedded manager checkbox deactivates a creator by mouse");
                        Check(detailed.Rows.Cast<DataGridViewRow>().Count(r => r.Tag is Creator) == 3, "Expanded window includes active and inactive creators");
                        var target = detailed.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == creator.Key);
                        typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(detailed, [new DataGridViewCellMouseEventArgs(detailed.Columns["Earlier"]!.Index, target.Index, 10, 10, new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0))]);
                        Check(settings.Creators[0].Key == creator.Key && Displayed().SequenceEqual([queueA, queueB]), "Expanded window arrow synchronizes main table and queue immediately");
                        var dragData = new DataObject(); dragData.SetData("Pawchive.CreatorOrder", creator.Key);
                        var dropRow = detailed.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is Creator c && c.Key == secondCreator.Key);
                        var dropPoint = detailed.PointToScreen(new Point(20, detailed.GetRowDisplayRectangle(dropRow.Index, false).Top + 20));
                        var drag = new DragEventArgs(dragData, 0, dropPoint.X, dropPoint.Y, DragDropEffects.Move, DragDropEffects.None);
                        typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(detailed, [drag]);
                        Check(drag.Effect == DragDropEffects.Move, "Active creator drag advertises a valid move target");
                        int invalidations = 0; InvalidateEventHandler countInvalidations = (_, _) => invalidations++;
                        detailed.Invalidated += countInvalidations;
                        for (int i = 0; i < 50; i++) typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(detailed, [drag]);
                        detailed.Invalidated -= countInvalidations;
                        Check(invalidations == 0, "Holding a dragged creator over the same target does not repaint every card repeatedly");
                        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(detailed, [drag]);
                        Check(settings.Creators[0].Key == secondCreator.Key && Displayed().SequenceEqual([queueB, queueA]), "Dropping a creator updates the shared queue and table");
                        Call(form, "MoveCreator", creator.Key, -1);
                        Field<Dictionary<string, string>>(form, "catalogDates")[creator.Key] = "12.11.2026 00:15";
                        Call(form, "FillAuthors");
                        Call(form, "SetDownloadingCreator", secondCreator.Key);
                        Check(Field<string>(form, "downloadingCreator") == secondCreator.Key, "Both creator views use the current real transfer identity");
                        using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "creator-manager.png"));
                    }
                    catch (Exception e) { failure = e; }
                    finally { Call(form, "SetDownloadingCreator", (object)null!); Call(form, "CloseAuthorManager"); }
                };
                managerTimer.Start(); Call(form, "ShowAuthorManager"); while (Field<Panel?>(form, "authorManager") is not null) await Task.Delay(20); if (failure is not null) throw failure;
                settings.Language = "en"; settings.Theme = "dark"; Call(form, "ApplyUiLanguage"); Call(form, "ApplyAppearance");
                using var englishManager = new System.Windows.Forms.Timer { Interval = 100 };
                englishManager.Tick += (_, _) =>
                {
                    var dialog = Field<Panel?>(form, "authorManager"); if (dialog is null) return; englishManager.Stop();
                    try
                    {
                        Check(dialog.Text == "Creators and download order" && All(dialog).OfType<ModernButton>().Any(b => b.Text == "Done"), "Expanded manager is localized in English");
                        Call(form, "SetDownloadingCreator", creator.Key);
                        using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "creator-manager-dark.png"));
                        Check(dialog.Visible && dialog.Parent!.Visible && dialog.Parent.Bounds == form.ClientRectangle, "Embedded backdrop covers the client area and manager stays visible inside it");
                        var backdrop = dialog.Parent!;
                        using var full = new Bitmap(backdrop.Width, backdrop.Height); backdrop.DrawToBitmap(full, new Rectangle(Point.Empty, full.Size)); full.Save(Path.Combine(AppContext.BaseDirectory, "creator-overlay-dark.png"));
                    }
                    catch (Exception e) { failure = e; }
                    finally { Call(form, "SetDownloadingCreator", (object)null!); Call(form, "CloseAuthorManager"); }
                };
                englishManager.Start(); Call(form, "ShowAuthorManager"); while (Field<Panel?>(form, "authorManager") is not null) await Task.Delay(20); if (failure is not null) throw failure;
                int controlCount = form.Controls.Count;
                Call(form, "ShowAuthorManager");
                var embedded = Field<Panel>(form, "authorManager");
                form.Width -= 50; form.PerformLayout();
                Check(Math.Abs(embedded.Left * 2 + embedded.Width - embedded.Parent!.ClientSize.Width) <= 1 && Math.Abs(embedded.Top * 2 + embedded.Height - embedded.Parent.ClientSize.Height) <= 1, "Embedded manager remains centered when the main window is resized");
                var escapeArgs = new object[] { default(Message), Keys.Escape };
                Check((bool)typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, escapeArgs)! && Field<Panel?>(form, "authorManager") is null, "Escape closes the embedded panel without closing the application");
                Check(form.Controls.Count == controlCount && form.OwnedForms.Length == 0, "Closing embedded manager removes the backdrop and creates no separate window");
                form.Width += 50;
                settings.Language = "ru"; Call(form, "ApplyUiLanguage");
                await Task.Delay(220);
                Check(Field<string?>(form, "downloadingCreator") is null, "Idle interface has no highlighted creator");
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "creator-workspace.png")); }
                Post Transfer(Creator c, string id, string published) => new() { Creator = c, Raw = JsonSerializer.SerializeToElement(new { id, title = id, published, has_full = true, detail_fetched = true, file = new { name = id + ".bin", path = "/queue/" + id + ".bin", server = "https://n1.pawchive.pw" } }) };
                var firstTransfer = Transfer(creator, "order-a", "2025-01-01"); var secondTransfer = Transfer(secondCreator, "order-b", "2026-01-01");
                posts.Clear(); posts.AddRange([secondTransfer, firstTransfer]); Call(form, "FillPosts");
                var seen = new System.Collections.Concurrent.ConcurrentQueue<(string Path, string? Creator)>();
                flowHandler.OnRequest = request => { if (request.RequestUri!.AbsolutePath.Contains("/queue/")) seen.Enqueue((request.RequestUri.AbsolutePath, Field<string?>(form, "downloadingCreator"))); };
                await (Task)Call(form, "DownloadPosts", CancellationToken.None)!; flowHandler.OnRequest = null;
                Check(seen.Select(r => r.Path).SequenceEqual(["/data/queue/order-a.bin", "/data/queue/order-b.bin"]), "Real media requests follow chosen creator order rather than insertion order or global date order");
                Check(seen.Select(r => r.Creator).SequenceEqual([creator.Key, secondCreator.Key]) && Field<string?>(form, "downloadingCreator") is null, "Highlight follows actual HTTP transfers and clears after queue completion");
                Func<Task> cancelHighlight = () => Task.FromException(new OperationCanceledException());
                try { await (Task)Call(form, "WithCreatorHighlight", firstTransfer, cancelHighlight)!; } catch (OperationCanceledException) { }
                Check(Field<string?>(form, "downloadingCreator") is null, "Cancellation always clears active creator highlight");
                Func<Task> failHighlight = () => Task.FromException(new IOException("test transfer failure"));
                try { await (Task)Call(form, "WithCreatorHighlight", firstTransfer, failHighlight)!; } catch (IOException) { }
                Check(Field<string?>(form, "downloadingCreator") is null, "Transfer error always clears active creator highlight");
                Post SkipFixture(string id, bool full) => new() { Creator = creator, Raw = JsonSerializer.SerializeToElement(new { id, title = id, published = "2026-11-12", has_full = true, detail_fetched = true, file = new { name = id + "-ready.bin", path = "/skip/" + id + "-ready.bin", server = "https://n1.pawchive.pw" }, attachments = full ? new[] { new { name = id + "-slow1.bin", path = "/skip/" + id + "-slow1.bin", server = "https://n1.pawchive.pw" }, new { name = id + "-slow2.bin", path = "/skip/" + id + "-slow2.bin", server = "https://n1.pawchive.pw" } } : [] }) };
                var preSaved = SkipFixture("skip-test", false);
                await new Downloader(Field<Api>(form, "api"), settings.Root, history).SavePost(preSaved, _ => { }, (long received, long? total) => { }, CancellationToken.None);
                var skippedPost = SkipFixture("skip-test", true); var afterSkipped = Transfer(secondCreator, "after-skip", "2026-11-12");
                posts.Clear(); posts.AddRange([skippedPost, afterSkipped]); Call(form, "FillPosts");
                int opened = 0; var streamsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                flowHandler.MediaContent = request => request.RequestUri!.AbsolutePath.Contains("-slow") ? new StreamContent(new SkippableStream(() => { if (Interlocked.Increment(ref opened) == 2) streamsStarted.TrySetResult(); })) : null;
                using var skipQueue = new CancellationTokenSource();
                var skipRun = (Task)Call(form, "DownloadPosts", skipQueue.Token)!;
                try
                {
                    Check(await Task.WhenAny(streamsStarted.Task, Task.Delay(5000)) == streamsStarted.Task, "Two attachments are receiving data before post skip");
                    Check(Field<ModernButton>(form, "skipPost").Enabled, "Skip button is enabled only for an active post");
                    Field<ModernButton>(form, "skipPost").PerformClick(); await skipRun;
                }
                finally { skipQueue.Cancel(); flowHandler.MediaContent = null; }
                Check(!history.Posts[skippedPost.Key].Complete && history.Posts[skippedPost.Key].Files.Count == 1 && history.Posts[afterSkipped.Key].Complete, "Skipping preserves finished file, leaves current post incomplete, and completes next post");
                Check(!Directory.EnumerateFiles(settings.Root, "*.part", SearchOption.AllDirectories).Any(), "Skipping cleans up both incomplete attachment files before continuing");
                Check(!Field<ModernButton>(form, "skipPost").Enabled && Field<CancellationTokenSource?>(form, "currentPostCancellation") is null, "End of queue disables skip and releases per-post cancellation source");
                Check(History.Load(settings.Root).State(settings.Root, skippedPost) == "Пропущен / повторить", "Skipped post remains identifiable and eligible after loading history again");
                await (Task)Call(form, "DownloadPosts", CancellationToken.None)!;
                Check(history.Posts[skippedPost.Key].Complete && history.Posts[skippedPost.Key].Files.Count == 3, "Next download run retries and completes skipped post");
                Check(flowHandler.Calls["/data/skip/skip-test-ready.bin"] == 1 && flowHandler.Calls["/data/queue/after-skip.bin"] == 1, "Retry keeps previously completed files and does not download subsequent completed post again");
                using var stopped = new CancellationTokenSource();
                using var stoppedPost = (CancellationTokenSource)Call(form, "BeginCurrentPost", stopped.Token)!;
                stopped.Cancel(); Call(form, "SkipCurrentPost");
                Check(stoppedPost.IsCancellationRequested && !Field<bool>(form, "postSkipRequested"), "Stop cancellation takes priority over post skip");
                Call(form, "EndCurrentPost", stoppedPost);
                Console.WriteLine($"PASS: {assertions} appearance, contrast, layout, settings, paging and progress checks.");
            }
            catch (Exception e) { failure = e; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) { Console.Error.WriteLine(failure); Environment.ExitCode = 1; }
    }
}
