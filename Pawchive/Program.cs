using System.Diagnostics;
using System.Text.Json;

namespace Pawchive;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(Locale.Error(e.Exception.Message), "Pawchive", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}

public partial class MainForm : Form
{
    readonly string settingsPath;
    Settings settings = new();
    readonly Api api;
    History history = new();
    readonly List<Post> posts = [];
    readonly HashSet<string> scanned = [];
    readonly HashSet<string> directPosts = [];
    readonly Dictionary<string, string> catalogDates = [];
    CancellationTokenSource? operation;
    bool closeAfterStop;
    readonly TextBox root = new() { Dock = DockStyle.Fill };
    readonly TextBox authorUrl = new() { Dock = DockStyle.Fill, PlaceholderText = "https://pawchive.pw/patreon/user/12345" };
    readonly TextBox postUrl = new() { Dock = DockStyle.Fill, PlaceholderText = "Ссылка на отдельный пост: https://pawchive.pw/…/user/…/post/…" };
    readonly ModernButton downloadPost = new() { Text = "Скачать этот пост", AutoSize = true };
    readonly ModernButton add = new() { Text = "Добавить автора", AutoSize = true };
    readonly ModernButton remove = new() { Text = "Убрать автора", AutoSize = true };
    readonly ModernButton scan = new() { Text = "Обновить новые", AutoSize = true, Height = 36 };
    readonly ModernButton fullScan = new() { Text = "Полная проверка", AutoSize = true, Height = 36 };
    readonly ModernButton download = new() { Text = "Скачать недостающее", AutoSize = true, Height = 36, Enabled = false };
    readonly ModernButton stop = new() { Text = "Остановить", AutoSize = true, Enabled = false };
    readonly ModernButton open = new() { Text = "Открыть папку", AutoSize = true };
    readonly ModernButton showHistory = new() { Text = "История", AutoSize = true };
    readonly Label status = new() { Text = "Выберите авторов и нажмите «Проверить посты».", AutoSize = true, MaximumSize = new(1100, 0) };
    readonly Label summary = new() { Text = "Проверка и скачивание запускаются только кнопками.", AutoSize = true };
    readonly ModernProgress progress = new() { Dock = DockStyle.Fill, Height = 10 };
    readonly Label transferSummary = new() { Text = "Одновременно скачивается не более двух файлов", Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly Label[] mediaLabels = [new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Файл 1 — ожидание" }, new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Файл 2 — ожидание" }];
    readonly ModernProgress[] mediaBars = [new() { Dock = DockStyle.Fill }, new() { Dock = DockStyle.Fill }];
    readonly DateTime[] lastMediaUpdates = new DateTime[2];
    readonly DataGridView authors = Grid();
    readonly DataGridView table = Grid();
    readonly ModernCombo filter = new() { Width = 210 };
    readonly ModernCombo authorFilter = new() { Width = 220, Tag = "creator-filter", AccessibleName = "Фильтр автора" };
    sealed record AuthorFilterItem(string? Key, string Label)
    {
        public override string ToString() => Label;
    }
    bool updatingAuthorFilter;
    const int PageSize = 200;
    int currentPage;
    int visiblePostCount;
    readonly ModernButton previousPage = new() { Text = "Назад", AutoSize = true, Enabled = false };
    readonly ModernButton nextPage = new() { Text = "Далее", AutoSize = true, Enabled = false };
    readonly Label pageLabel = new() { AutoSize = true, Padding = new(5, 4, 5, 0) };
    readonly Dictionary<string, DataGridViewRow> postRows = [];
    bool loading;

    static DataGridView Grid() => new WorkspaceGrid()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoGenerateColumns = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        RowTemplate = new FastRow { Height = 30 }, EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(237, 241, 246), ForeColor = Color.FromArgb(35, 45, 60) },
        DefaultCellStyle = new() { SelectionBackColor = Color.FromArgb(218, 232, 250), SelectionForeColor = Color.Black },
        AlternatingRowsDefaultCellStyle = new() { BackColor = Color.FromArgb(248, 250, 252) }
    };
    public MainForm(Api? apiOverride = null, string? settingsFile = null)
    {
        api = apiOverride ?? new Api();
        settingsPath = settingsFile ?? Path.Combine(AppContext.BaseDirectory, "settings.json");
        BuildWorkspace();
        add.Click += (_, _) => ShowAddAuthor();
        remove.Click += (_, _) => RemoveCreator();
        scan.Click += async (_, _) => await Run(token => ScanPosts(token, false));
        fullScan.Click += async (_, _) => await Run(token => ScanPosts(token, true));
        download.Click += async (_, _) => await Run(DownloadPosts);
        downloadPost.Click += async (_, _) => await ShowDownloadPost();
        skipPost.Click += (_, _) => SkipCurrentPost();
        stop.Click += (_, _) => { operation?.Cancel(); stop.Enabled = false; skipPost.Enabled = false; status.Text = "Останавливаю. Завершённые файлы останутся в истории…"; };
        open.Click += (_, _) => { if (Directory.Exists(root.Text)) OpenPath(root.Text); };
        showHistory.Click += async (_, _) => await ShowHistory();
        filter.SelectedIndexChanged += (_, _) => { if (changingLanguage) return; currentPage = 0; FillPosts(); };
        authorFilter.SelectedIndexChanged += (_, _) => { if (changingLanguage || updatingAuthorFilter) return; currentPage = 0; FillPosts(); };
        previousPage.Click += (_, _) => { if (currentPage > 0) currentPage--; FillPosts(); };
        nextPage.Click += (_, _) => { currentPage++; FillPosts(); };
        table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && table.Rows[e.RowIndex].Tag is Post p) OpenPath(p.Url); };
        FormClosing += (_, e) =>
        {
            if (operation is not null) { closeAfterStop = true; operation.Cancel(); e.Cancel = true; status.Text = "Сохраняю историю и закрываю приложение…"; }
            else { SaveSettings(); api.Dispose(); }
        };
        Shown += async (_, _) => await LoadSettings();
    }
    static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    async Task LoadSettings()
    {
        if (File.Exists(settingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? throw new IOException("Пустые настройки");
                if (settings.Creators == null || string.IsNullOrWhiteSpace(settings.Root)) throw new IOException("Некорректные настройки");
            }
            catch (Exception e)
            {
                MessageBox.Show(Locale.Error("Не удалось прочитать настройки. Будут использованы исходные авторы. История скачиваний хранится отдельно.\r\n" + e.Message), Locale.T("Настройки"));
                // Keep the damaged settings for recovery before the next save.
                File.Copy(settingsPath, settingsPath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                settings = new();
            }
        }
        ApplyUiLanguage(); ApplyAppearance();
        root.Text = settings.Root;
        SetBusy(true); stop.Enabled = false;
        try { await LoadLibrary(); }
        catch (Exception e) { Error(e.Message); }
        finally { SetBusy(false); progress.Style = ProgressBarStyle.Blocks; ActiveControl = table; }
    }
    async Task LoadLibrary(string? requestedRoot = null)
    {
        status.Text = "Открываю сохранённый список постов…";
        var libraryRoot = requestedRoot ?? settings.Root;
        var loaded = await Task.Run(() => (History: History.Load(libraryRoot), Catalogs: Catalog.LoadAll(libraryRoot)));
        settings.Root = libraryRoot; root.Text = libraryRoot;
        history = loaded.History;
        posts.Clear(); scanned.Clear(); directPosts.Clear(); catalogDates.Clear(); currentPage = 0;
        foreach (var catalog in loaded.Catalogs)
        {
            var creator = settings.Creators.FirstOrDefault(c => c.Key == catalog.Creator.Key);
            if (creator != null)
            {
                posts.AddRange(catalog.ToPosts(creator with { Name = catalog.Creator.Name }));
                if (catalog.Complete) scanned.Add(creator.Key);
                catalogDates[creator.Key] = CatalogDate(catalog.CheckedAt);
            }
            else posts.AddRange(catalog.ToPosts().Where(p => catalog.DirectPostIds.Contains(p.Id)));
            foreach (var id in catalog.DirectPostIds) directPosts.Add(catalog.Creator.Key + "/" + id);
        }
        await RefreshStates(); FillAuthors(); FillPosts();
        status.Text = posts.Count > 0 ? $"Открыто из сохранённого списка: {posts.Count} постов. Можно скачивать сразу; «Обновить новые» проверит сайт." : "Сохранённого списка пока нет. Нажмите «Обновить новые» для первой полной проверки.";
    }
    static string CatalogDate(string value) => DateTimeOffset.TryParse(value, out var date) ? date.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Отдельные посты";
    async Task RefreshStates()
    {
        var current = posts.ToArray(); var currentHistory = history; var libraryRoot = settings.Root;
        await Task.Run(() =>
        {
            foreach (var post in current)
            {
                try { post.Warning = currentHistory.Posts.TryGetValue(post.Key, out var saved) ? saved.Error : ""; post.State = currentHistory.State(libraryRoot, post, settings.DownloadPreviews); post.Assets(settings.DownloadPreviews); post.Links(); }
                catch (Exception e) { post.Error = e.Message; post.State = "Ошибка"; }
            }
        });
    }
    void SaveSettings()
    {
        try { Storage.WriteJson(settingsPath, settings); }
        catch (Exception e) { status.Text = "Не удалось сохранить настройки: " + e.Message; }
    }
    void FillAuthors() => RefreshCreatorViews(false);
    void AddCreator()
    {
        try
        {
            var c = Creator.Parse(authorUrl.Text);
            if (settings.Creators.Any(a => a.Key == c.Key)) { status.Text = "Этот автор уже в списке."; return; }
            settings.Creators.Add(c); authorUrl.Clear(); FillAuthors(); SaveSettings(); currentPage = 0; FillPosts();
        }
        catch (Exception e) { Error(e.Message); }
    }
    void RemoveCreator()
    {
        if (authors.CurrentRow?.Tag is not Creator creator) return;
        settings.Creators.RemoveAll(c => c.Key == creator.Key);
        posts.RemoveAll(p => p.Creator.Key == creator.Key); scanned.Remove(creator.Key);
        FillAuthors(); FillPosts(); SaveSettings();
        status.Text = "Автор убран из списка. Скачанные файлы и история сохранены.";
    }
    async Task<bool> ChangeRoot(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            SetBusy(true); stop.Enabled = false;
            await LoadLibrary(path); SaveSettings();
            return true;
        }
        catch (Exception e) { root.Text = settings.Root; Error(e.Message); return false; }
        finally { SetBusy(false); progress.Style = ProgressBarStyle.Blocks; }
    }
    List<Creator> Selected() => settings.Creators.Where(c => c.Selected).ToList();
    async Task Run(Func<CancellationToken, Task> action, bool requireAuthors = true)
    {
        if (operation is not null) return;
        if (root.Text.Trim() != settings.Root && !await ChangeRoot(root.Text.Trim())) return;
        if (requireAuthors && Selected().Count == 0) { status.Text = "Отметьте хотя бы одного автора."; return; }
        operation = new(); SetBusy(true);
        FileStream? sessionLock = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(settings.Root, ".pawchive"));
            try { sessionLock = new(Path.Combine(settings.Root, ".pawchive", "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new IOException("Эта папка сейчас используется другим экземпляром приложения. Дождитесь окончания его работы."); }
            history = await Task.Run(() => History.Load(settings.Root));
            SaveSettings(); await action(operation.Token);
        }
        catch (OperationCanceledException) { status.Text = "Остановлено. Завершённые файлы сохранены; следующий запуск скачает недостающее."; }
        catch (Exception e) { Error(e.Message); }
        finally
        {
            await RefreshStates();
            SetDownloadingCreator(null);
            sessionLock?.Dispose(); operation.Dispose(); operation = null;
            SetBusy(false); FillPosts(); progress.Style = ProgressBarStyle.Blocks;
            foreach (var bar in mediaBars) bar.Style = ProgressBarStyle.Blocks;
            if (closeAfterStop) Close();
        }
    }
    void SetBusy(bool busy)
    {
        scan.Enabled = fullScan.Enabled = !busy; download.Enabled = !busy && posts.Count > 0;
        root.Enabled = add.Enabled = remove.Enabled = authorUrl.Enabled = authors.Enabled = !busy;
        postUrl.Enabled = downloadPost.Enabled = !busy;
        filter.Enabled = !busy; authorFilter.Enabled = !busy && authorFilter.Items.Count > 0; stop.Enabled = busy;
        previousPage.Enabled = !busy && currentPage > 0;
        nextPage.Enabled = !busy && (currentPage + 1) * PageSize < visiblePostCount;
        if (expandedAuthors is not null) expandedAuthors.Enabled = !busy;
        if (!busy) { skipPost.Enabled = false; SetDownloadingCreator(null); authors.Invalidate(); }
        if (busy) progress.Style = ProgressBarStyle.Marquee;
    }
    void ResetTransfers(string caption)
    {
        transferSummary.Text = caption;
        progress.Style = ProgressBarStyle.Blocks; progress.Value = 0;
        for (var slot = 0; slot < 2; slot++)
        {
            mediaNames[slot] = ""; mediaLabels[slot].Text = Locale.T($"Файл {slot + 1}"); mediaDetails[slot].Text = "Ожидание";
            mediaLabels[slot].ForeColor = palette.Text;
            mediaIcons[slot].Glyph = AppIcon.File; mediaIcons[slot].Invalidate();
            mediaBars[slot].Style = ProgressBarStyle.Blocks; mediaBars[slot].Value = 0;
            mediaErrors[slot] = false;
            lastMediaUpdates[slot] = DateTime.MinValue;
        }
    }
    void MediaUpdate(Post post, int index, int count, MediaProgress update)
    {
        var slot = update.Slot;
        if (update.Stage == "Скачивание" && (DateTime.UtcNow - lastMediaUpdates[slot]).TotalMilliseconds < 120) return;
        lastMediaUpdates[slot] = DateTime.UtcNow;
        var percent = update.Total > 0 ? (int)Math.Clamp(update.Received * 100.0 / update.Total.Value, 0, 100) : (int?)null;
        var bytes = $"{update.Received / 1048576.0:0.0}" + (update.Total.HasValue ? $" / {update.Total.Value / 1048576.0:0.0}" : "") + " МБ";
        mediaNames[slot] = update.Name; mediaLabels[slot].Text = update.Name.StartsWith("Превью: ", StringComparison.Ordinal) ? Locale.T(update.Name) : update.Name;
        mediaDetails[slot].Text = update.Stage + (percent.HasValue ? $" • {percent}%" : "") + (update.Stage is "Скачивание" or "Готово" or "Уже скачан" ? " • " + bytes : "");
        mediaLabels[slot].ForeColor = update.Stage == "Ошибка" ? palette.Error : palette.Text;
        mediaErrors[slot] = update.Stage == "Ошибка";
        mediaIcons[slot].Glyph = IconDrawing.FileType(update.Name); mediaIcons[slot].Invalidate();
        mediaBars[slot].Style = percent.HasValue ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
        if (update.Stage == "Ошибка") { mediaBars[slot].Style = ProgressBarStyle.Blocks; mediaBars[slot].Value = 0; }
        if (percent.HasValue) mediaBars[slot].Value = percent.Value;
        progress.Style = ProgressBarStyle.Blocks;
        progress.Value = update.Count > 0 ? Math.Clamp(update.Finished * 100 / update.Count, 0, 100) : 100;
        transferSummary.Text = $"Пост {index}/{count} • Файлов скачано: {update.Finished}/{update.Count} • {post.Creator.Name} — {post.Title}";
        post.State = update.Stage == "Распаковка" ? "Распаковка…" : $"Файлы: {update.Finished}/{update.Count}";
        RefreshRow(post);
    }
    async Task DownloadSpecificPost(CancellationToken token)
    {
        var address = PostAddress.Parse(postUrl.Text);
        status.Text = "Получаю выбранный пост…";
        var post = await api.GetPost(address, token);
        await Catalog.SavePost(settings.Root, post, true);
        directPosts.Add(post.Key);
        var previous = posts.FindIndex(p => p.Key == post.Key);
        if (previous >= 0) posts[previous] = post; else posts.Add(post);
        await RefreshStates(); FillPosts();
        if (!settings.RecheckDownloaded && history.State(settings.Root, post, settings.DownloadPreviews) == "Скачан") { ResetTransfers("Этот пост уже скачан"); progress.Value = 100; status.Text = "Все файлы этого поста уже сохранены."; return; }
        ResetTransfers("Скачиваю один пост — " + post.Title);
        status.Text = "Скачиваю файлы выбранного поста…";
        using var postCancellation = BeginCurrentPost(token);
        var downloader = new Downloader(api, settings.Root, history, settings.DownloadPreviews);
        try
        {
            await WithCreatorHighlight(post, () => SaveMedia(downloader, post, 1, 1, postCancellation.Token));
            postCancellation.Token.ThrowIfCancellationRequested();
            FinishPost(post);
            status.Text = post.State == "Скачан" ? $"Файлы сохранены. Без доступной полной версии: {post.SkippedAssets().Count}. Подробности в post.txt." : "Пост сохранён частично. Доступные архивы обработаны; подробности в post.txt папки поста.";
            if (post.Warning.Length > 0) Log(post.Url + " — " + post.Warning);
        }
        catch (OperationCanceledException) when (postSkipRequested && !token.IsCancellationRequested)
        { await RecordSkippedPost(downloader, post); token.ThrowIfCancellationRequested(); status.Text = "Пост пропущен. Следующий запуск скачает недостающее."; }
        catch (Exception e) when (e is not OperationCanceledException)
        { post.Error = e.Message; post.State = "Ошибка"; Log(post.Url + " — " + e.Message); throw; }
        finally { EndCurrentPost(postCancellation); }
    }
    void AuthorStatus(string key, string message)
    {
        creatorPostCounts[key] = posts.Count(p => p.Creator.Key == key);
        foreach (var grid in new[] { authors, expandedAuthors }.OfType<DataGridView>())
            foreach (DataGridViewRow r in grid.Rows) if (r.Tag is Creator c && c.Key == key) r.Cells["State"].Value = message;
    }
    void FinishPost(Post post)
    {
        post.Error = "";
        var entry = history.Posts[post.Key]; post.Warning = entry.Error;
        post.State = entry.Complete ? "Скачан" : post.Incomplete ? "Неполный пост" : entry.FileErrors.Count > 0 ? "Частично скачан" : entry.Files.Values.Any(f => f.ExtractionError.Length > 0) ? "Нужна распаковка" : "Частично скачан";
        progress.Value = entry.ExpectedFiles > 0 ? Math.Clamp((entry.ExpectedFiles - entry.FileErrors.Count) * 100 / entry.ExpectedFiles, 0, 100) : 100;
    }
    async Task SaveMedia(Downloader downloader, Post post, int index, int count, CancellationToken token)
    {
        bool acceptingProgress = true;
        IProgress<string> messages = new Progress<string>(text => { if (!IsDisposed && acceptingProgress && !token.IsCancellationRequested) status.Text = text; });
        IProgress<MediaProgress> updates = new Progress<MediaProgress>(p => { if (!IsDisposed && acceptingProgress && !token.IsCancellationRequested) MediaUpdate(post, index, count, p); });
        var lastSent = new long[2];
        try { await Task.Run(() => downloader.SavePost(post, text => messages.Report(text), p =>
        {
            var now = Environment.TickCount64;
            if (p.Stage == "Скачивание" && now - Volatile.Read(ref lastSent[p.Slot]) < 120) return;
            Interlocked.Exchange(ref lastSent[p.Slot], now);
            updates.Report(p);
        }, token), token); }
        finally { acceptingProgress = false; }
    }
    async Task ScanPosts(CancellationToken token, bool full)
    {
        int failed = 0;
        foreach (var c in Selected())
        {
            token.ThrowIfCancellationRequested();
            AuthorStatus(c.Key, "Проверяю…"); status.Text = "Проверяю посты: " + c.Name;
            try
            {
                Creator creator;
                try { creator = await api.Profile(c, token); }
                catch (Exception e) when (e is not OperationCanceledException) { creator = c; }
                var catalog = await api.Discover(creator, settings.Root, full, count => { status.Text = $"{creator.Name}: проверено {count} постов…"; AuthorStatus(c.Key, $"Проверено {count}…"); }, token);
                var result = catalog.ToPosts(creator);
                var idx = settings.Creators.FindIndex(a => a.Key == c.Key);
                settings.Creators[idx] = creator;
                foreach (DataGridViewRow r in authors.Rows) if (r.Tag is Creator a && a.Key == c.Key) { r.Tag = creator; r.Cells["Author"].Value = creator.Name; }
                posts.RemoveAll(p => p.Creator.Key == c.Key); posts.AddRange(result); scanned.Add(c.Key);
                catalogDates[c.Key] = CatalogDate(catalog.CheckedAt);
                AuthorStatus(c.Key, $"{result.Count} постов • {catalogDates[c.Key]}");
                await RefreshStates(); FillPosts();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++; AuthorStatus(c.Key, "Ошибка: " + e.Message); Log(c.Name + ": " + e.Message);
            }
        }
        SaveSettings(); UpdateSummary();
        status.Text = failed == 0 ? "Список сохранён. Можно скачивать сейчас или после перезапуска приложения." : $"Ошибок авторов: {failed}. Прежние сохранённые списки доступны. Подробности в .pawchive/errors.log.";
    }
    async Task DownloadPosts(CancellationToken token)
    {
        var selected = Selected().Select(c => c.Key).ToHashSet();
        var unscanned = Selected().Where(c => !scanned.Contains(c.Key)).ToList();
        if (unscanned.Count > 0)
        {
            status.Text = "Сначала проверьте всех отмеченных авторов: " + string.Join(", ", unscanned.Select(c => c.Name)); return;
        }
        await RefreshStates();
        var pending = OrderedPosts(posts.Where(p => selected.Contains(p.Creator.Key) && (settings.RecheckDownloaded || p.State != "Скачан"))).ToList();
        if (pending.Count == 0) { status.Text = "Все найденные посты уже скачаны. Для поиска новых нажмите «Обновить новые»."; return; }
        var downloader = new Downloader(api, settings.Root, history, settings.DownloadPreviews);
        int done = 0, failed = 0, partial = 0, skipped = 0;
        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var post = pending[i];
                using var postCancellation = BeginCurrentPost(token);
                SetDownloadingCreator(post.Creator.Key);
                try
                {
                    if (settings.RecheckDownloaded || post.Incomplete || post.Error.Length > 0)
                    {
                        status.Text = $"Получаю актуальные данные поста {i + 1}/{pending.Count}: {post.Title}";
                        var detail = await api.Detail(post, postCancellation.Token);
                        posts[posts.IndexOf(post)] = detail; post = detail;
                        await Catalog.SavePost(settings.Root, post);
                    }
                    post.State = "Скачивается…"; RefreshRow(post);
                    ResetTransfers($"Пост {i + 1}/{pending.Count}: {post.Creator.Name} — {post.Title}");
                    status.Text = $"Пост {i + 1}/{pending.Count}: {post.Creator.Name} — {post.Title}";
                    await SaveMedia(downloader, post, i + 1, pending.Count, postCancellation.Token);
                    postCancellation.Token.ThrowIfCancellationRequested();
                    FinishPost(post);
                    if (post.State == "Скачан") done++; else { partial++; Log(post.Url + " — " + post.Warning); }
                }
                catch (OperationCanceledException) when (postSkipRequested && !token.IsCancellationRequested)
                { await RecordSkippedPost(downloader, post); token.ThrowIfCancellationRequested(); skipped++; }
                catch (Exception e) when (e is not OperationCanceledException)
                { post.Error = e.Message; post.State = "Ошибка"; failed++; Log(post.Url + " — " + e.Message); }
                finally { EndCurrentPost(postCancellation); }
                RefreshRow(post); UpdateSummary();
            }
        }
        finally { SetDownloadingCreator(null); }
        status.Text = $"Готово: полностью {done}, частично {partial}, ошибок постов {failed}." + (failed + partial > 0 ? " Повторное нажатие загрузит недостающее." : "") + (skipped > 0 ? $" Пропущено постов: {skipped}. Следующий запуск попробует скачать их снова." : "");
        if (skipped > 0) status.Text = $"Готово: полностью {done}, частично {partial}, ошибок постов {failed}, пропущено {skipped}. Следующий запуск попробует скачать пропущенные посты снова.";
    }
    IEnumerable<Post> WorkspacePosts()
    {
        var creators = settings.Creators.ToDictionary(c => c.Key, c => c.Selected);
        // Standalone posts stay visible until their creator is added to the selectable list.
        return posts.Where(p => creators.TryGetValue(p.Creator.Key, out var selected) ? selected : directPosts.Contains(p.Key));
    }
    void UpdateAuthorFilter()
    {
        var key = authorFilter.Items.Count > 1 ? (authorFilter.SelectedItem as AuthorFilterItem)?.Key : null;
        var selected = Selected();
        updatingAuthorFilter = true;
        authorFilter.BeginUpdate();
        try
        {
            authorFilter.Items.Clear();
            if (selected.Count > 1) authorFilter.Items.Add(new AuthorFilterItem(null, Locale.T("Все выбранные авторы")));
            foreach (var creator in selected) authorFilter.Items.Add(new AuthorFilterItem(creator.Key, creator.Name));
            var index = authorFilter.Items.Cast<AuthorFilterItem>().ToList().FindIndex(item => item.Key == key);
            authorFilter.SelectedIndex = index >= 0 ? index : authorFilter.Items.Count > 0 ? 0 : -1;
            authorFilter.Enabled = operation is null && selected.Count > 0;
        }
        finally { authorFilter.EndUpdate(); updatingAuthorFilter = false; }
    }
    void FillPosts()
    {
        UpdateAuthorFilter();
        var authorKey = (authorFilter.SelectedItem as AuthorFilterItem)?.Key;
        table.SuspendLayout(); table.Rows.Clear(); postRows.Clear();
        var view = OrderedPosts(WorkspacePosts().Where(post => authorKey is null || post.Creator.Key == authorKey).Where(post => !(filter.SelectedIndex == 1 && post.State == "Скачан" || filter.SelectedIndex == 2 && post.State != "Скачан" || filter.SelectedIndex == 3 && !post.State.Contains("Ошибка") && post.State != "Неполный пост" && post.Warning.Length == 0))).ToList();
        visiblePostCount = view.Count;
        currentPage = Math.Clamp(currentPage, 0, Math.Max(0, (view.Count - 1) / PageSize));
        previousPage.Enabled = operation is null && currentPage > 0;
        nextPage.Enabled = operation is null && (currentPage + 1) * PageSize < view.Count;
        pageLabel.Text = view.Count == 0 ? "Нет постов" : $"{currentPage * PageSize + 1}–{Math.Min((currentPage + 1) * PageSize, view.Count)} из {view.Count}";
        var rows = new List<DataGridViewRow>();
        foreach (var post in view.Skip(currentPage * PageSize).Take(PageSize))
        {
            int files = 0, links = 0;
            try { files = post.Assets(settings.DownloadPreviews).Count; links = post.Links().Count; } catch { }
            var row = new FastRow { Height = (int)(38 * DeviceDpi / 96f) };
            row.CreateCells(table, post.Creator.Name, post.Published.Length >= 10 ? post.Published[..10] : "", post.Title, files, links, post.SkippedAssets().Count, Locale.T(post.State));
            row.Tag = post; postRows[post.Key] = row; rows.Add(row);
        }
        if (rows.Count > 0) table.Rows.AddRange(rows.ToArray());
        foreach (DataGridViewColumn column in table.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var row in rows) PaintRow(row, (Post)row.Tag!);
        table.ResumeLayout(); UpdateSummary(); UpdatePostFolder();
    }
    void RefreshRow(Post post)
    {
        if (!postRows.TryGetValue(post.Key, out var row)) return;
        row.Tag = post; row.Cells["State"].Value = Locale.T(post.State);
        row.Cells["Author"].Value = post.Creator.Name;
        row.Cells["Title"].Value = post.Title; row.Cells["Files"].Value = post.Assets(settings.DownloadPreviews).Count;
        row.Cells["Links"].Value = post.Links().Count; row.Cells["Skipped"].Value = post.SkippedAssets().Count;
        PaintRow(row, post); UpdatePostFolder();
    }
    void PaintRow(DataGridViewRow row, Post post)
    {
        row.Cells["Author"].ToolTipText = post.Creator.Name;
        row.Cells["Title"].ToolTipText = post.Title;
        var color = post.State == "Скачан" ? palette.Good : post.State.Contains("Ошибка") || post.State == "Частично скачан" ? palette.Error : post.State is "Не скачан" or "Неполный пост" ? palette.Muted : palette.AccentInk;
        row.Cells["State"].Style.ForeColor = color;
        row.Cells["State"].Style.SelectionForeColor = AppPalette.Readable(color, palette.Selection);
        row.Cells["State"].ToolTipText = Locale.Error(post.Error.Length > 0 ? post.Error : post.Warning);
        row.Cells["Skipped"].ToolTipText = string.Join("\r\n", post.SkippedAssets().Select(s => s.Name + ": " + Locale.T(s.Reason)));
    }
    void UpdateSummary()
    {
        var selected = Selected().Select(c => c.Key).ToHashSet();
        var current = WorkspacePosts().ToList();
        var done = current.Count(p => p.State == "Скачан");
        summary.Text = $"Выбрано авторов: {selected.Count}   •   Найдено постов: {current.Count}   •   Скачано: {done}   •   Осталось: {current.Count - done}";
        libraryCaption.Text = $"{current.Count:N0} постов · Скачано {done:N0}";
        folderCaption();
        if (operation is null) download.Enabled = current.Count > 0;
    }
    void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(settings.Root, ".pawchive", "errors.log"), DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine); } catch { }
    }
    void Error(string message) { UiLanguage.SetError(status, message); MessageBox.Show(this, Locale.Error(message), "Pawchive", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    async Task ShowHistory()
    {
        try
        {
            var data = await Task.Run(() => History.Load(settings.Root));
            using var dialog = new Form { Text = "История — " + settings.Root, Size = new(1000, 550), StartPosition = FormStartPosition.CenterParent, Font = Font, Icon = BrandAssets.Icon };
            var grid = Grid(); grid.ReadOnly = true;
            grid.Columns.Add(new FastTextColumn { HeaderText = "Автор", Width = 170 });
            grid.Columns.Add(new FastTextColumn { HeaderText = "Пост", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new FastTextColumn { HeaderText = "Статус", Width = 130 });
            grid.Columns.Add(new FastTextColumn { HeaderText = "Файлов", Width = 70 });
            grid.Columns.Add(new FastTextColumn { HeaderText = "Завершён", Width = 170 });
            var entries = data.Posts.Values.OrderByDescending(p => p.CompletedAt).ToList(); int page = 0;
            var pager = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new(8) };
            var previous = new ModernButton { Text = "Назад", Width = 96, Glyph = AppIcon.Left }; var next = new ModernButton { Text = "Далее", Width = 96, Glyph = AppIcon.Right }; var caption = new Label { AutoSize = true, Padding = new(5, 5, 0, 0) };
            pager.Controls.AddRange([previous, next, caption]);
            void RenderHistory()
            {
                grid.Rows.Clear();
                foreach (var entry in entries.Skip(page * PageSize).Take(PageSize))
                {
                    var idx = grid.Rows.Add(entry.Author, entry.Title, Locale.T(entry.Complete ? "Скачан" : entry.FileErrors.Count > 0 ? "Частично" : "Не завершён"), entry.ExpectedFiles > 0 ? $"{Math.Min(entry.Files.Count, entry.ExpectedFiles)}/{entry.ExpectedFiles}" : entry.Files.Count.ToString(), DateTimeOffset.TryParse(entry.CompletedAt, out var date) ? date.ToLocalTime().ToString(Locale.DateTimeFormat) : "");
                    grid.Rows[idx].Tag = entry; grid.Rows[idx].Cells[2].ToolTipText = Locale.Error(entry.Error);
                }
                previous.Enabled = page > 0; next.Enabled = (page + 1) * PageSize < entries.Count;
                caption.Text = entries.Count == 0 ? "История пуста" : $"{page * PageSize + 1}–{Math.Min((page + 1) * PageSize, entries.Count)} из {entries.Count}";
            }
            previous.Click += (_, _) => { page--; RenderHistory(); }; next.Click += (_, _) => { page++; RenderHistory(); }; RenderHistory();
            grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is PostEntry p && Directory.Exists(Storage.Inside(settings.Root, p.Folder))) OpenPath(Storage.Inside(settings.Root, p.Folder)); };
            dialog.Controls.Add(new GridHost(grid) { Dock = DockStyle.Fill }); dialog.Controls.Add(pager); dialog.MinimumSize = new(800, 420);
            ThemeTree(dialog); UiLanguage.Bind(dialog); WindowsAppearance.TitleBar(dialog, palette); dialog.HandleCreated += (_, _) => WindowsAppearance.TitleBar(dialog, palette); dialog.ShowDialog(this);
        }
        catch (Exception e) { Error(e.Message); }
    }
}

