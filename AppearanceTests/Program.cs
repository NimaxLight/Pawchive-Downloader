using System.Reflection;
using System.Text.Json;
using Pawchive;
using System.Net;

class FlowHandler : HttpMessageHandler
{
    public object? FullPost;
    public Dictionary<string, int> Calls = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string path = request.RequestUri!.AbsolutePath; Calls[path] = Calls.GetValueOrDefault(path) + 1;
        var content = path.Contains("/post/") ? new StringContent(JsonSerializer.Serialize(FullPost), System.Text.Encoding.UTF8, "application/json") : new StringContent("original file content", System.Text.Encoding.UTF8, "application/octet-stream");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

static class Program
{
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
                Console.WriteLine($"PASS: {assertions} appearance, contrast, layout, settings, paging and progress checks.");
            }
            catch (Exception e) { failure = e; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) { Console.Error.WriteLine(failure); Environment.ExitCode = 1; }
    }
}
