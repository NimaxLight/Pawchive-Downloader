using System.Globalization;
using Microsoft.Win32;

namespace Pawchive;

public partial class MainForm
{
    AppPalette palette = AppPalette.Create(false, Color.FromArgb(125, 86, 166));
    readonly ModernButton appearance = new() { Text = "Настройки", Glyph = AppIcon.Settings, Quiet = true, Width = 130 };
    readonly ModernButton openPostFolder = new() { Text = "Папка поста", Glyph = AppIcon.Folder, Enabled = false };
    readonly Label libraryCaption = new() { AutoSize = true, Text = "Сохранённая библиотека", Tag = "muted" };
    readonly IconView[] mediaIcons = [new(), new()];
    readonly Label[] mediaDetails = [new(), new()];
    readonly bool[] mediaErrors = new bool[2];
    readonly ToolTip tips = new() { AutoPopDelay = 12000 };
    readonly List<Control> surfaceControls = [];
    bool changingLanguage;
    readonly string[] mediaNames = ["", ""];
    void BuildWorkspace()
    {
        Text = "Pawchive Downloader 1.9.2";
        Icon = BrandAssets.Icon;
        Font = new("Segoe UI", 10);
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new(1180, 780); Size = new(1420, 940);
        StartPosition = FormStartPosition.CenterScreen;
        var shell = Compose(1, 4); shell.RowStyles.Add(new(SizeType.Absolute, 72)); shell.RowStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Absolute, 178)); shell.RowStyles.Add(new(SizeType.Absolute, 64)); Controls.Add(shell);
        var header = Compose(2, 1); header.Padding = new(22, 14, 22, 14); header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 360)); Surface(header);
        var brand = Compose(2, 1); brand.ColumnStyles.Add(new(SizeType.Absolute, 32)); brand.ColumnStyles.Add(new(SizeType.Percent, 100));
        brand.Controls.Add(new BrandView { Dock = DockStyle.Fill, Margin = new(0, 8, 8, 8) }, 0, 0);
        brand.Controls.Add(new Label { Text = Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new("Segoe UI Semibold", 13) }, 1, 0);
        header.Controls.Add(brand, 0, 0);
        var headerActions = Compose(2, 1); headerActions.ColumnStyles.Add(new(SizeType.Percent, 100)); headerActions.ColumnStyles.Add(new(SizeType.Absolute, 138));
        ButtonStyle(open, "Downloads", AppIcon.Folder, 210, quiet: true); ButtonStyle(appearance, "Настройки", AppIcon.Settings, 130, quiet: true);
        open.Dock = appearance.Dock = DockStyle.Fill; headerActions.Controls.Add(open, 0, 0); headerActions.Controls.Add(appearance, 1, 0); header.Controls.Add(headerActions, 1, 0); shell.Controls.Add(header, 0, 0);
        var body = Compose(2, 1); body.ColumnStyles.Add(new(SizeType.Absolute, 370)); body.ColumnStyles.Add(new(SizeType.Percent, 100)); shell.Controls.Add(body, 0, 1);
        var sidebar = Compose(1, 4); sidebar.Padding = new(14, 16, 14, 14); sidebar.RowStyles.Add(new(SizeType.Absolute, 48)); sidebar.RowStyles.Add(new(SizeType.Percent, 100)); sidebar.RowStyles.Add(new(SizeType.Absolute, 50)); sidebar.RowStyles.Add(new(SizeType.Absolute, 94));
        var authorHeading = Compose(3, 1); authorHeading.ColumnStyles.Add(new(SizeType.Percent, 100)); authorHeading.ColumnStyles.Add(new(SizeType.Absolute, 44)); authorHeading.ColumnStyles.Add(new(SizeType.Absolute, 40));
        authorHeading.Controls.Add(new Label { Text = "Авторы", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new("Segoe UI Semibold", 11) }, 0, 0);
        ButtonStyle(add, "", AppIcon.Plus, 38, quiet: true); add.AccessibleName = "Добавить автора"; tips.SetToolTip(add, "Добавить автора по ссылке"); authorHeading.Controls.Add(add, 2, 0);
        ButtonStyle(expandAuthors, "↗", AppIcon.None, 38, quiet: true); expandAuthors.Margin = Padding.Empty; tips.SetToolTip(expandAuthors, "Расширить список авторов"); expandAuthors.Click += (_, _) => ShowAuthorManager(); authorHeading.Controls.Add(expandAuthors, 1, 0); sidebar.Controls.Add(authorHeading, 0, 0);
        add.Margin = Padding.Empty;
        ConfigureCreatorGrid(authors, false);
        sidebar.Controls.Add(new GridHost(authors) { Dock = DockStyle.Fill }, 0, 1);
        sidebar.Controls.Add(new Label { Text = "Отметьте авторов для проверки\nи скачивания", Dock = DockStyle.Fill, Tag = "muted", Font = new("Segoe UI", 9), Padding = new(4, 10, 0, 0) }, 0, 2);
        var sidebarActions = Compose(1, 2); sidebarActions.RowStyles.Add(new(SizeType.Percent, 50)); sidebarActions.RowStyles.Add(new(SizeType.Percent, 50));
        ButtonStyle(remove, "Убрать автора", AppIcon.Trash, 205, quiet: true); ButtonStyle(showHistory, "История", AppIcon.History, 205, quiet: true);
        remove.Dock = showHistory.Dock = DockStyle.Fill; remove.Margin = showHistory.Margin = new(0, 3, 0, 6); sidebarActions.Controls.Add(remove, 0, 0); sidebarActions.Controls.Add(showHistory, 0, 1); sidebar.Controls.Add(sidebarActions, 0, 3); body.Controls.Add(sidebar, 0, 0);
        var main = Compose(1, 5); main.Padding = new(22, 16, 22, 8); main.RowStyles.Add(new(SizeType.Absolute, 64)); main.RowStyles.Add(new(SizeType.Absolute, 50)); main.RowStyles.Add(new(SizeType.Absolute, 58)); main.RowStyles.Add(new(SizeType.Percent, 100)); main.RowStyles.Add(new(SizeType.Absolute, 30));
        var titleRow = Compose(3, 1); titleRow.ColumnStyles.Add(new(SizeType.Percent, 100)); titleRow.ColumnStyles.Add(new(SizeType.Absolute, 164)); titleRow.ColumnStyles.Add(new(SizeType.Absolute, 185));
        var title = Compose(1, 2); title.RowStyles.Add(new(SizeType.Absolute, 32)); title.RowStyles.Add(new(SizeType.Percent, 100));
        title.Controls.Add(new Label { Text = "Посты", AutoSize = true, Font = new("Segoe UI Semibold", 17), Margin = Padding.Empty }, 0, 0); title.Controls.Add(libraryCaption, 0, 1);
        ButtonStyle(openPostFolder, "Папка поста", AppIcon.Folder, 154); ButtonStyle(downloadPost, "Пост по ссылке", AppIcon.Link, 175); titleRow.Controls.Add(title, 0, 0); titleRow.Controls.Add(openPostFolder, 1, 0); titleRow.Controls.Add(downloadPost, 2, 0); main.Controls.Add(titleRow, 0, 0);
        openPostFolder.Click += (_, _) => { if (SelectedPostFolder() is string folder) OpenPath(folder); };
        table.SelectionChanged += (_, _) => UpdatePostFolder();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new(0, 2, 0, 0) };
        ButtonStyle(scan, "Обновить новые", AppIcon.Refresh, 174); ButtonStyle(download, "Скачать недостающее", AppIcon.Download, 236, primary: true); ButtonStyle(fullScan, "Полная проверка", AppIcon.Scan, 176, quiet: true);
        tips.SetToolTip(fullScan, "Проверить все посты выбранных авторов заново"); actions.Controls.AddRange([scan, download, fullScan]); main.Controls.Add(actions, 0, 1);
        var filters = Compose(5, 1); filters.Padding = new(0, 5, 0, 7); filters.ColumnStyles.Add(new(SizeType.Absolute, 205)); filters.ColumnStyles.Add(new(SizeType.Absolute, 230)); filters.ColumnStyles.Add(new(SizeType.Percent, 100)); filters.ColumnStyles.Add(new(SizeType.Absolute, 42)); filters.ColumnStyles.Add(new(SizeType.Absolute, 42));
        filter.Items.AddRange(["Все посты", "Требуют скачивания", "Скачанные", "Ошибки"]); filter.SelectedIndex = 0; filter.Dock = DockStyle.Top; filter.Margin = new(0, 3, 10, 0);
        authorFilter.Dock = DockStyle.Top; authorFilter.Margin = new(0, 3, 10, 0);
        pageLabel.Dock = DockStyle.Fill; pageLabel.AutoSize = false; pageLabel.TextAlign = ContentAlignment.MiddleRight; pageLabel.Padding = new(0, 0, 12, 0); pageLabel.Tag = "muted";
        ButtonStyle(previousPage, "", AppIcon.Left, 36, quiet: true); previousPage.AccessibleName = "Предыдущая страница"; ButtonStyle(nextPage, "", AppIcon.Right, 36, quiet: true); nextPage.AccessibleName = "Следующая страница";
        filters.Controls.Add(filter, 0, 0); filters.Controls.Add(authorFilter, 1, 0); filters.Controls.Add(pageLabel, 2, 0); filters.Controls.Add(previousPage, 3, 0); filters.Controls.Add(nextPage, 4, 0); main.Controls.Add(filters, 0, 2);
        table.ReadOnly = true; table.AccessibleName = "Посты"; table.RowTemplate.Height = 38;
        table.Columns.Add(new FastTextColumn { Name = "Author", HeaderText = "Автор", Width = 112 }); table.Columns.Add(new FastTextColumn { Name = "Date", HeaderText = "Дата", Width = 112 });
        table.Columns.Add(new FastTextColumn { Name = "Title", HeaderText = "Пост", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 130 });
        table.Columns.Add(new FastTextColumn { Name = "Files", HeaderText = "Файлы", Width = 62 }); table.Columns.Add(new FastTextColumn { Name = "Links", HeaderText = "Ссылки", Width = 64 }); table.Columns.Add(new FastTextColumn { Name = "Skipped", HeaderText = "Пропуски", Width = 82 }); table.Columns.Add(new FastTextColumn { Name = "State", HeaderText = "Статус", Width = 165 });
        foreach (var name in new[] { "Files", "Links", "Skipped" }) table.Columns[name]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        var tableFrame = new GridHost(table) { Dock = DockStyle.Fill, Padding = new(1), Outline = true, Margin = Padding.Empty }; Surface(tableFrame); main.Controls.Add(tableFrame, 0, 3);
        main.Controls.Add(new Label { Text = "Двойной щелчок — открыть пост на сайте", Dock = DockStyle.Fill, Tag = "muted", Font = new("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft }, 0, 4); body.Controls.Add(main, 1, 0);
        var transfers = Compose(1, 4); transfers.Padding = new(22, 10, 22, 14); transfers.RowStyles.Add(new(SizeType.Absolute, 42)); transfers.RowStyles.Add(new(SizeType.Absolute, 28)); transfers.RowStyles.Add(new(SizeType.Percent, 100)); transfers.RowStyles.Add(new(SizeType.Absolute, 7)); Surface(transfers);
        var transferTitle = Compose(3, 1); transferTitle.ColumnStyles.Add(new(SizeType.Percent, 100)); transferTitle.ColumnStyles.Add(new(SizeType.Absolute, 190)); transferTitle.ColumnStyles.Add(new(SizeType.Absolute, 155));
        transferTitle.Controls.Add(new Label { Text = "Текущие загрузки", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new("Segoe UI Semibold", 11) }, 0, 0); ButtonStyle(stop, "Остановить", AppIcon.Stop, 150); ButtonStyle(skipPost, "Пропустить пост", AppIcon.Right, 182); transferTitle.Controls.Add(skipPost, 1, 0); transferTitle.Controls.Add(stop, 2, 0); transfers.Controls.Add(transferTitle, 0, 0);
        transferSummary.Tag = "muted"; transfers.Controls.Add(transferSummary, 0, 1);
        var files = Compose(2, 1); files.ColumnStyles.Add(new(SizeType.Percent, 50)); files.ColumnStyles.Add(new(SizeType.Percent, 50));
        for (int i = 0; i < 2; i++)
        {
            var file = Compose(2, 3); file.Margin = i == 0 ? new(0, 0, 14, 10) : new(14, 0, 0, 10); file.ColumnStyles.Add(new(SizeType.Absolute, 28)); file.ColumnStyles.Add(new(SizeType.Percent, 100)); file.RowStyles.Add(new(SizeType.Percent, 100)); file.RowStyles.Add(new(SizeType.Absolute, 24)); file.RowStyles.Add(new(SizeType.Absolute, 8));
            mediaIcons[i].Dock = DockStyle.Fill; mediaIcons[i].Margin = new(0, 6, 9, 6); mediaLabels[i].TextAlign = ContentAlignment.MiddleLeft;
            mediaLabels[i].Text = "Файл " + (i + 1); mediaDetails[i].Text = "Ожидание"; mediaDetails[i].Dock = DockStyle.Fill; mediaDetails[i].Tag = "muted"; mediaDetails[i].Font = new("Segoe UI", 9); mediaDetails[i].AutoEllipsis = true;
            file.Controls.Add(mediaIcons[i], 0, 0); file.Controls.Add(mediaLabels[i], 1, 0); file.Controls.Add(mediaDetails[i], 1, 1); file.Controls.Add(mediaBars[i], 0, 2); file.SetColumnSpan(mediaBars[i], 2); files.Controls.Add(file, i, 0);
            mediaBars[i].AccessibleName = $"Прогресс файла {i + 1}";
        }
        transfers.Controls.Add(files, 0, 2); progress.AccessibleName = "Прогресс файлов текущего поста"; transfers.Controls.Add(progress, 0, 3); shell.Controls.Add(transfers, 0, 2);
        var footer = Compose(1, 2); footer.Padding = new(22, 5, 22, 5); footer.RowStyles.Add(new(SizeType.Absolute, 23)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        summary.AutoSize = false; summary.Dock = DockStyle.Fill; summary.AutoEllipsis = true; summary.Tag = "muted"; summary.Font = new("Segoe UI", 9);
        status.AutoSize = false; status.Dock = DockStyle.Fill; status.MaximumSize = Size.Empty; status.AutoEllipsis = true; status.Font = new("Segoe UI", 9); status.Text = "Выберите авторов и нажмите «Обновить новые».";
        footer.Controls.Add(summary, 0, 0); footer.Controls.Add(status, 0, 1); shell.Controls.Add(footer, 0, 3);
        appearance.Click += async (_, _) => await ShowAppearance();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged; Disposed += (_, _) => { SystemEvents.UserPreferenceChanged -= SystemThemeChanged; tips.Dispose(); };
        ApplyAppearance();
        UiLanguage.Bind(this, open, mediaLabels[0], mediaLabels[1]);
    }
    static TableLayoutPanel Compose(int columns, int rows)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty };
        if (rows == 1) layout.RowStyles.Add(new(SizeType.Percent, 100));
        if (columns == 1) layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        return layout;
    }
    void Surface(Control control) => surfaceControls.Add(control);
    static void ButtonStyle(ModernButton button, string text, AppIcon glyph, int width, bool primary = false, bool quiet = false)
    { button.AutoSize = false; button.Text = text; button.Glyph = glyph; button.Size = new(width, 38); button.Primary = primary; button.Quiet = quiet; button.Anchor = AnchorStyles.Left | AnchorStyles.Top; button.Margin = new(0, 0, 8, 0); }
    void folderCaption()
    {
        open.Text = Path.GetFileName(settings.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(open.Text)) open.Text = Locale.T("Папка загрузок");
        tips.SetToolTip(open, settings.Root); open.AccessibleName = Locale.T("Открыть папку загрузок: " + settings.Root);
    }
    void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        try { BeginInvoke(() => { if (!IsDisposed && !Disposing) ApplyAppearance(); }); } catch (InvalidOperationException) { }
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x320 or 0x31A && IsHandleCreated && !IsDisposed) ApplyAppearance();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowsAppearance.TitleBar(this, palette); }
    void ApplyAppearance()
    {
        download.Text = settings.RecheckDownloaded ? "Проверить и докачать" : "Скачать недостающее";
        palette = WindowsAppearance.Read(settings); ThemeTree(this); WindowsAppearance.TitleBar(this, palette);
        foreach (Form owned in OwnedForms) { ThemeTree(owned); WindowsAppearance.TitleBar(owned, palette); }
        foreach (DataGridViewRow row in table.Rows) if (row.Tag is Post p) PaintRow(row, p);
        for (int i = 0; i < 2; i++) mediaLabels[i].ForeColor = mediaErrors[i] ? palette.Error : palette.Text;
        authors.Invalidate();
    }
    void ThemeTree(Control control)
    {
        bool surface = surfaceControls.Contains(control);
        control.BackColor = control is Form ? palette.Background : surface || control is TextBox or ComboBox or DataGridView or TextFieldFrame ? palette.Surface : control.Parent?.BackColor ?? palette.Background;
        control.ForeColor = control.Tag as string == "muted" ? palette.Muted : palette.Text;
        if (control is ModernButton button) { button.Palette = palette; button.Invalidate(); }
        if (control is ModernProgress bar) { bar.Palette = palette; bar.Invalidate(); }
        if (control is ModernCombo combo) { combo.Palette = palette; combo.Invalidate(); }
        if (control is TextFieldFrame textFrame) { textFrame.Palette = palette; textFrame.Invalidate(); }
        if (control is ModernCheckBox check) { check.Palette = palette; check.Invalidate(); }
        if (control is ThemeScrollBar scroll) { scroll.Palette = palette; scroll.Invalidate(); }
        if (control is SurfacePanel panel) { panel.LineColor = palette.Border; panel.Invalidate(); }
        if (control is DataGridView grid)
        {
            bool side = grid == authors || grid == expandedAuthors; grid.BackgroundColor = side ? palette.Background : palette.Surface; grid.GridColor = palette.Border;
            grid.CellBorderStyle = side ? DataGridViewCellBorderStyle.None : DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = (int)(38 * DeviceDpi / 96f);
            grid.DefaultCellStyle.BackColor = side ? palette.Background : palette.Surface; grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection; grid.DefaultCellStyle.SelectionForeColor = AppPalette.Readable(palette.Text, palette.Selection);
            grid.DefaultCellStyle.Padding = new(8, 0, 4, 0);
            grid.RowsDefaultCellStyle = new() { BackColor = side ? palette.Background : palette.Surface, ForeColor = palette.Text, SelectionBackColor = palette.Selection, SelectionForeColor = AppPalette.Readable(palette.Text, palette.Selection), Padding = new(8, 0, 4, 0) };
            grid.AlternatingRowsDefaultCellStyle = new() { BackColor = side ? palette.Background : AppPalette.Blend(palette.Surface, palette.Soft, .3) };
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Soft; grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Muted; grid.ColumnHeadersDefaultCellStyle.Padding = new(8, 0, 0, 0);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Soft; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9);
            grid.ColumnHeadersDefaultCellStyle.Padding = new(4, 0, 0, 0);
            grid.RowHeadersDefaultCellStyle.BackColor = palette.Surface;
        }
        foreach (Control child in control.Controls) ThemeTree(child);
    }
    Form Dialog(string title, Size size)
    {
        var form = new Form { Text = title, ClientSize = size, MinimumSize = new(size.Width + 16, size.Height + 39), Font = Font, Icon = BrandAssets.Icon, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, AutoScaleDimensions = new(96, 96), AutoScaleMode = AutoScaleMode.Dpi };
        form.HandleCreated += (_, _) => WindowsAppearance.TitleBar(form, palette); return form;
    }
    string? AskUrl(string title, string label, string placeholder, string action, Func<string, string> validate)
    {
        using var dialog = Dialog(title, new(640, 220)); var layout = Compose(1, 4); layout.Padding = new(24);
        layout.RowStyles.Add(new(SizeType.Absolute, 30)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 38));
        layout.Controls.Add(new Label { Text = label, AutoSize = true }, 0, 0);
        var input = new TextBox { BorderStyle = BorderStyle.None, PlaceholderText = placeholder, AccessibleName = label, Margin = Padding.Empty };
        var inputFrame = new TextFieldFrame(input) { Dock = DockStyle.Top, Margin = Padding.Empty };
        layout.Controls.Add(inputFrame, 0, 1);
        var error = new Label { Dock = DockStyle.Fill, AutoEllipsis = true }; layout.Controls.Add(error, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var accept = new ModernButton { Text = action, Width = 165, Primary = true }; var cancel = new ModernButton { Text = "Отмена", Width = 105, DialogResult = DialogResult.Cancel }; buttons.Controls.AddRange([accept, cancel]); layout.Controls.Add(buttons, 0, 3); dialog.Controls.Add(layout);
        accept.Click += (_, _) => { try { validate(input.Text); dialog.DialogResult = DialogResult.OK; } catch (Exception e) { UiLanguage.SetError(error, e.Message); error.ForeColor = palette.Error; } };
        dialog.AcceptButton = accept; dialog.CancelButton = cancel; ThemeTree(dialog); UiLanguage.Bind(dialog); dialog.Shown += (_, _) => input.Focus();
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }
    void ShowAddAuthor()
    {
        var url = AskUrl("Добавить автора", "Ссылка на страницу автора", "https://pawchive.pw/patreon/user/12345", "Добавить", value => { var c = Creator.Parse(value); if (settings.Creators.Any(a => a.Key == c.Key)) throw new ArgumentException("Этот автор уже в списке."); return c.Url; });
        if (url is not null) { authorUrl.Text = url; AddCreator(); }
    }
    async Task ShowDownloadPost()
    {
        var url = AskUrl("Скачать пост по ссылке", "Ссылка на конкретный пост", "https://pawchive.pw/patreon/user/123/post/456", "Скачать", value => PostAddress.Parse(value).Id);
        if (url is not null) { postUrl.Text = url; await Run(DownloadSpecificPost, false); }
    }
    async Task ShowAppearance()
    {
        using var dialog = Dialog("Настройки", new(620, 640)); var layout = Compose(1, 10); layout.Padding = new(24);
        var settingsBody = Compose(1, 2); settingsBody.RowStyles.Add(new(SizeType.Absolute, 90)); settingsBody.RowStyles.Add(new(SizeType.Percent, 100));
        var languageRow = Compose(1, 2); languageRow.Padding = new(24, 16, 24, 0); languageRow.RowStyles.Add(new(SizeType.Absolute, 28)); languageRow.RowStyles.Add(new(SizeType.Percent, 100));
        var language = new ModernCombo { Dock = DockStyle.Top, Margin = Padding.Empty, Tag = "language-options", AccessibleName = "Язык интерфейса" };
        language.Items.AddRange(["Русский", "English"]); language.SelectedIndex = settings.Language == "en" ? 1 : 0;
        languageRow.Controls.Add(new Label { Text = "Язык интерфейса", AutoSize = true }, 0, 0); languageRow.Controls.Add(language, 0, 1);
        settingsBody.Controls.Add(languageRow, 0, 0); settingsBody.Controls.Add(layout, 0, 1);
        foreach (int height in new[] { 28, 50, 28, 50, 28, 58, 28, 92, 102, 38 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        var theme = new ModernCombo { Dock = DockStyle.Top, Margin = Padding.Empty }; theme.Items.AddRange(["Как в Windows", "Светлая", "Тёмная"]); theme.SelectedIndex = settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        layout.Controls.Add(new Label { Text = "Тема", AutoSize = true }, 0, 0); layout.Controls.Add(theme, 0, 1);
        layout.Controls.Add(new Label { Text = "Основной цвет", AutoSize = true }, 0, 2); var colorRow = Compose(2, 1); colorRow.ColumnStyles.Add(new(SizeType.Percent, 100)); colorRow.ColumnStyles.Add(new(SizeType.Absolute, 155));
        var mode = new ModernCombo { Dock = DockStyle.Top, Margin = new(0, 0, 12, 0) }; mode.Items.AddRange(["Как в Windows", "Выбрать свой"]); mode.SelectedIndex = settings.AccentMode == "custom" ? 1 : 0;
        string selectedColor = settings.AccentColor; var color = new ModernButton { Text = "Выбрать цвет…", Width = 150, Enabled = mode.SelectedIndex == 1 };
        mode.SelectedIndexChanged += (_, _) => color.Enabled = mode.SelectedIndex == 1;
        color.Click += (_, _) => { using var picker = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml(selectedColor) }; if (picker.ShowDialog(dialog) == DialogResult.OK) { selectedColor = ColorTranslator.ToHtml(picker.Color); if (!selectedColor.StartsWith('#')) selectedColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; color.Text = selectedColor; } };
        colorRow.Controls.Add(mode, 0, 0); colorRow.Controls.Add(color, 1, 0); layout.Controls.Add(colorRow, 0, 3);
        layout.Controls.Add(new Label { Text = "Папка загрузок и истории", AutoSize = true }, 0, 4); var folderRow = Compose(2, 1); folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.Absolute, 155));
        var path = new TextBox { Text = settings.Root, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var pathFrame = new SurfacePanel { Height = 38, Dock = DockStyle.Top, Padding = new(8, 8, 8, 6), Outline = true, Margin = new(0, 0, 12, 0) }; pathFrame.Controls.Add(path);
        var choose = new ModernButton { Text = "Изменить…", Width = 150, Glyph = AppIcon.Folder, Enabled = operation is null };
        choose.Click += (_, _) => { using var picker = new FolderBrowserDialog { SelectedPath = path.Text, Description = Locale.T("Папка для файлов и истории скачиваний"), UseDescriptionForTitle = true }; if (picker.ShowDialog(dialog) == DialogResult.OK) path.Text = picker.SelectedPath; };
        folderRow.Controls.Add(pathFrame, 0, 0); folderRow.Controls.Add(choose, 1, 0); layout.Controls.Add(folderRow, 0, 5);
        layout.Controls.Add(new Label { Text = "Загрузка", AutoSize = true }, 0, 6);
        var options = Compose(1, 2); options.RowStyles.Add(new(SizeType.Percent, 50)); options.RowStyles.Add(new(SizeType.Percent, 50));
        var previews = new ModernCheckBox { Text = "Скачивать превью изображений", Checked = settings.DownloadPreviews, Enabled = operation is null, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var recheck = new ModernCheckBox { Text = "Повторно проверять скачанные посты", Checked = settings.RecheckDownloaded, Enabled = operation is null, Dock = DockStyle.Fill, Margin = Padding.Empty };
        options.Controls.Add(previews, 0, 0); options.Controls.Add(recheck, 0, 1); layout.Controls.Add(options, 0, 7);
        layout.Controls.Add(new Label { Text = "Превью сохраняются отдельно от оригиналов в подпапку previews.\nПовторная проверка заново получает сведения о постах,\nно не перекачивает готовые файлы. Запуск — кнопкой скачивания.\nВ режиме «Как в Windows» оформление меняется вместе с системой.", Dock = DockStyle.Fill, Tag = "muted", Font = new("Segoe UI", 9) }, 0, 8);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty }; var save = new ModernButton { Text = "Сохранить", Primary = true, Width = 130, DialogResult = DialogResult.OK }; var cancel = new ModernButton { Text = "Отмена", Width = 110, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([save, cancel]); layout.Controls.Add(actions, 0, 9); dialog.Controls.Add(settingsBody); dialog.AcceptButton = save; dialog.CancelButton = cancel; ThemeTree(dialog); UiLanguage.Bind(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (path.Text != settings.Root && !await ChangeRoot(path.Text)) return;
        settings.Theme = theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" }; settings.AccentMode = mode.SelectedIndex == 1 ? "custom" : "system"; settings.AccentColor = selectedColor;
        bool changedPreviews = settings.DownloadPreviews != previews.Checked;
        settings.DownloadPreviews = previews.Checked; settings.RecheckDownloaded = recheck.Checked;
        settings.Language = language.SelectedIndex == 1 ? "en" : "ru";
        ApplyUiLanguage();
        SaveSettings(); ApplyAppearance();
        if (changedPreviews) { await RefreshStates(); FillPosts(); }
    }
    string? SelectedPostFolder()
    {
        if (table.CurrentRow?.Tag is not Post post) return null;
        PostEntry? entry; lock (history.SyncRoot) history.Posts.TryGetValue(post.Key, out entry);
        if (entry is null || entry.Folder.Length == 0) return null;
        try { var folder = Storage.Inside(settings.Root, entry.Folder); return Directory.Exists(folder) ? folder : null; }
        catch (IOException) { return null; }
    }
    void UpdatePostFolder()
    {
        var folder = SelectedPostFolder(); openPostFolder.Enabled = folder is not null;
        tips.SetToolTip(openPostFolder, folder ?? Locale.T("Выберите полностью или частично скачанный пост"));
    }
    void ApplyUiLanguage()
    {
        changingLanguage = true;
        try
        {
            Locale.Language = settings.Language;
            UiLanguage.Refresh(this);
            tips.SetToolTip(add, Locale.T("Добавить автора по ссылке"));
            tips.SetToolTip(expandAuthors, Locale.T("Расширить список авторов"));
            tips.SetToolTip(fullScan, Locale.T("Проверить все посты выбранных авторов заново"));
            for (int i = 0; i < mediaLabels.Length; i++) mediaLabels[i].Text = mediaNames[i].Length == 0 ? Locale.T($"Файл {i + 1}") : mediaNames[i].StartsWith("Превью: ", StringComparison.Ordinal) ? Locale.T(mediaNames[i]) : mediaNames[i];
            var selectedPost = (table.CurrentRow?.Tag as Post)?.Key;
            var selectedCreator = (authors.CurrentRow?.Tag as Creator)?.Key;
            FillAuthors(); FillPosts();
            foreach (DataGridViewRow row in table.Rows) if (row.Tag is Post post && post.Key == selectedPost) table.CurrentCell = row.Cells["Title"];
            foreach (DataGridViewRow row in authors.Rows) if (row.Tag is Creator creator && creator.Key == selectedCreator) authors.CurrentCell = row.Cells["Author"];
            folderCaption(); UpdatePostFolder(); authors.Invalidate();
        }
        finally { changingLanguage = false; }
    }
}
