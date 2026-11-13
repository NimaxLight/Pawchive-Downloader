using System.Globalization;
namespace Pawchive;
public partial class MainForm
{
    readonly HashSet<string> pickedPosts = [];
    readonly TextBox dateFrom = new() { PlaceholderText = "дд.мм.гггг", AccessibleName = "С даты" };
    readonly TextBox dateTo = new() { PlaceholderText = "дд.мм.гггг", AccessibleName = "По дату включительно" };
    readonly ModernButton pickMatching = new() { Text = "Отметить все подходящие" };
    readonly ModernButton clearPicked = new() { Text = "Снять отметки" };
    readonly Label pickedCaption = new() { AutoSize = true, Tag = "muted" };
    readonly ModernButton clearPeriod = new() { Text = "Убрать период", Width = 150, Height = 38, Enabled = false };
    readonly Label selectionHint = new() { Text = "Автор и статус — фильтры просмотра", Dock = DockStyle.Fill, Tag = "muted", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9) };
    Post[]? downloadPlan;
    Panel? downloadManager;
    CreatorBackdrop? downloadOverlay;
    DownloadMode chosenMode;
    ToolStripDropDown? datePopup;
    string? lastPickedKey;
    bool IsNewPost(Post p) => p.IsNew && p.State != "Скачан";
    static bool ParseDate(string text, out DateOnly? value)
    {
        value = null; if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateOnly.TryParseExact(text.Trim(), ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "ddMMyyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.Year is < 1900 or > 2199) return false;
        value = parsed; return true;
    }
    bool Period(out DateOnly? from, out DateOnly? to)
    {
        bool first = ParseDate(dateFrom.Text, out from), last = ParseDate(dateTo.Text, out to);
        return first && last && (from is null || to is null || from <= to);
    }
    string PeriodCaption()
    {
        if (!Period(out var from, out var to)) return Locale.T("Проверьте даты над таблицей.");
        if (from is null && to is null) return Locale.T("Без ограничения по дате");
        return (from?.ToString("dd.MM.yyyy") ?? "…") + " — " + (to?.ToString("dd.MM.yyyy") ?? "…");
    }
    IEnumerable<Post> VisiblePostView()
    {
        if (!Period(out var from, out var to)) return [];
        var authorKey = (authorFilter.SelectedItem as AuthorFilterItem)?.Key;
        return OrderedPosts(WorkspacePosts().Where(p => DownloadSelection.InPeriod(p, from, to) && (authorKey is null || p.Creator.Key == authorKey)).Where(p => filter.SelectedIndex switch
        { 1 => p.State != "Скачан", 2 => p.State == "Скачан", 3 => DownloadSelection.IsProblem(p), 4 => IsNewPost(p), _ => true }));
    }
    List<Post> BuildDownloadPlan(DownloadMode mode)
    {
        if (!Period(out var from, out var to)) return [];
        return DownloadSelection.Build(OrderedPosts(posts), Selected().Select(c => c.Key).ToHashSet(), mode, pickedPosts, from, to, settings.RecheckDownloaded);
    }
    Control BuildDateControls()
    {
        var section = new SectionPanel { Dock = DockStyle.Fill, Edges = RuleEdges.Top | RuleEdges.Bottom, Padding = new Padding(0, 8, 0, 7), Margin = new Padding(0, 0, 0, 5) };
        var layout = Compose(1, 2); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); section.Controls.Add(layout);
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        heading.Controls.Add(new Label { Text = "Период публикации", AutoSize = true, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Margin = new Padding(0, 0, 14, 0) });
        heading.Controls.Add(new Label { Text = "Для таблицы и загрузки", AutoSize = true, Tag = "muted", Font = new Font(Font.FontFamily, 9), Margin = new Padding(0, 2, 0, 0) });
        layout.Controls.Add(heading, 0, 0);
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        layout.Controls.Add(row, 0, 1);
        foreach (var input in new[] { dateFrom, dateTo })
        {
            var group = Compose(1, 2); group.Size = new Size(190, 58); group.Margin = new Padding(0, 0, 12, 0);
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            group.Controls.Add(new Label { Text = input == dateFrom ? "С даты" : "По дату включительно", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9), Tag = "muted", Margin = Padding.Empty }, 0, 0);
            var entry = new DateEntry(input, input == dateFrom ? "Календарь даты начала" : "Календарь даты окончания") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            entry.CalendarButton.Click += (_, _) => OpenDateCalendar(input, entry); group.Controls.Add(entry, 0, 1); row.Controls.Add(group);
            input.TextChanged += (_, _) => clearPeriod.Enabled = dateFrom.Text.Length > 0 || dateTo.Text.Length > 0;
            input.Leave += (_, _) => { if (ParseDate(input.Text, out var value)) input.Text = value?.ToString("dd.MM.yyyy") ?? ""; currentPage = 0; FillPosts(); };
            input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ActiveControl = table; e.Handled = e.SuppressKeyPress = true; } };
        }
        clearPeriod.Margin = new Padding(0, 20, 0, 0);
        clearPeriod.Click += (_, _) => { dateFrom.Clear(); dateTo.Clear(); currentPage = 0; FillPosts(); }; row.Controls.Add(clearPeriod);
        tips.SetToolTip(dateFrom, "С даты — для таблицы и загрузки"); tips.SetToolTip(dateTo, "По дату включительно — для таблицы и загрузки");
        return section;
    }
    void OpenDateCalendar(TextBox input, Control anchor)
    {
        datePopup?.Close(); datePopup?.Dispose();
        ParseDate(input.Text, out var value);
        var popup = new CalendarPopup(palette);
        datePopup = popup;
        var calendar = new DateCalendar(value, chosen => { input.Text = chosen?.ToString("dd.MM.yyyy") ?? ""; popup.Close(); currentPage = 0; FillPosts(); }, ThemeTree);
        calendar.Size = new Size((int)(350 * DeviceDpi / 96f), (int)(360 * DeviceDpi / 96f));
        popup.Items.Add(new ToolStripControlHost(calendar) { AutoSize = false, Size = calendar.Size, Margin = Padding.Empty, Padding = Padding.Empty });
        popup.Size = new Size(calendar.Width + popup.Padding.Horizontal, calendar.Height + popup.Padding.Vertical);
        popup.Show(anchor, new Point(0, anchor.Height + (int)(6 * DeviceDpi / 96f)));
    }
    Control BuildPickControls()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        pickMatching.Width = 270; pickMatching.Height = clearPicked.Height = 34; clearPicked.Width = 155;
        pickMatching.Click += (_, _) => { foreach (var p in VisiblePostView()) pickedPosts.Add(p.Key); FillPosts(); };
        clearPicked.Click += (_, _) => { pickedPosts.Clear(); FillPosts(); };
        row.Padding = new Padding(0, 5, 0, 5); row.Controls.AddRange([pickMatching, clearPicked]); return row;
    }
    Control BuildPostFooter()
    {
        var footer = Compose(2, 2); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        pickedCaption.AutoSize = false; pickedCaption.Dock = DockStyle.Fill; pickedCaption.TextAlign = ContentAlignment.MiddleLeft; pickedCaption.Margin = Padding.Empty;
        footer.Controls.Add(pickedCaption, 0, 0); footer.Controls.Add(selectionHint, 1, 0);
        var hint = new Label { Text = "Двойной щелчок — открыть пост на сайте", Dock = DockStyle.Fill, Tag = "muted", Font = new Font(Font.FontFamily, 9) };
        footer.Controls.Add(hint, 0, 1); footer.SetColumnSpan(hint, 2); return footer;
    }
    void UpdatePickedCaption()
    {
        int matching = VisiblePostView().Count();
        pickMatching.Text = $"Отметить все подходящие ({matching})"; pickMatching.Enabled = matching > 0;
        bool valid = Period(out var from, out var to);
        pickedCaption.Text = valid ? $"Отмечено: {pickedPosts.Count} · В периоде: {posts.Count(p => pickedPosts.Contains(p.Key) && DownloadSelection.InPeriod(p, from, to))}" : "Проверьте даты над таблицей.";
        clearPicked.Enabled = pickedPosts.Count > 0;
        clearPeriod.Enabled = dateFrom.Text.Length > 0 || dateTo.Text.Length > 0;
    }
    void ConfigurePostPicking()
    {
        table.Columns.Insert(0, new DataGridViewCheckBoxColumn { Name = "Picked", HeaderText = "✓", Width = 32, ReadOnly = true, Resizable = DataGridViewTriState.False });
        table.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != table.Columns["Picked"]!.Index || e.Button != MouseButtons.Left || table.Rows[e.RowIndex].Tag is not Post p) return;
            TogglePostPick(p, ModifierKeys.HasFlag(Keys.Shift));
        };
        table.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space && table.CurrentRow?.Tag is Post p) { TogglePostPick(p, e.Shift); e.Handled = e.SuppressKeyPress = true; } };
        table.CellPainting += (_, e) => PaintPostChoice(e);
    }
    void TogglePostPick(Post p, bool extend)
    {
        bool mark = !pickedPosts.Contains(p.Key); var view = VisiblePostView().ToList();
        int first = view.FindIndex(item => item.Key == lastPickedKey), last = view.FindIndex(item => item.Key == p.Key);
        var range = extend && first >= 0 && last >= 0 ? view.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1) : [p];
        foreach (var item in range) { if (mark) pickedPosts.Add(item.Key); else pickedPosts.Remove(item.Key); }
        if (!extend) lastPickedKey = p.Key;
        foreach (DataGridViewRow row in table.Rows) if (row.Tag is Post item) row.Cells["Picked"].Value = pickedPosts.Contains(item.Key);
        table.InvalidateColumn(table.Columns["Picked"]!.Index); UpdatePickedCaption();
    }
    void PaintPostChoice(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == table.Columns["Picked"]!.Index && table.Rows[e.RowIndex].Tag is Post picked)
        {
            e.PaintBackground(e.CellBounds, true); float scale = DeviceDpi / 96f; var r0 = e.CellBounds;
            var box = new Rectangle(r0.X + (r0.Width - (int)(14 * scale)) / 2, r0.Y + (r0.Height - (int)(14 * scale)) / 2, (int)(14 * scale), (int)(14 * scale));
            bool check = pickedPosts.Contains(picked.Key);
            using var checkBrush = new SolidBrush(check ? palette.Accent : palette.Surface); using var pen = new Pen(check ? palette.Accent : palette.Muted);
            using var rounded = IconDrawing.Rounded(box, (int)(3 * scale)); e.Graphics!.FillPath(checkBrush, rounded); e.Graphics.DrawPath(pen, rounded);
            if (check) { using var tick = new Pen(palette.AccentText, 1.8f * scale); e.Graphics.DrawLines(tick, [new PointF(box.X + 2 * scale, box.Y + 6 * scale), new(box.X + 5 * scale, box.Y + 9 * scale), new(box.X + 11 * scale, box.Y + 3 * scale)]); }
            e.Handled = true; return;
        }
        if (e.RowIndex < 0 || e.ColumnIndex != table.Columns["Title"]!.Index || table.Rows[e.RowIndex].Tag is not Post p || !IsNewPost(p)) return;
        e.PaintBackground(e.CellBounds, true); var r = e.CellBounds; int width = TextRenderer.MeasureText(Locale.T("Новое"), Font).Width + 4;
        var badge = new Rectangle(r.X + 6, r.Y + 8, width, r.Height - 16); using var brush = new SolidBrush(palette.Selection); e.Graphics!.FillRectangle(brush, badge);
        TextRenderer.DrawText(e.Graphics, Locale.T("Новое"), Font, badge, AppPalette.Readable(palette.AccentInk, palette.Selection), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        var text = new Rectangle(badge.Right + 6, r.Y, Math.Max(0, r.Right - badge.Right - 12), r.Height);
        TextRenderer.DrawText(e.Graphics, p.Title, Font, text, e.State.HasFlag(DataGridViewElementStates.Selected) ? AppPalette.Readable(palette.Text, palette.Selection) : palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }
    void CloseDownloadChoice()
    {
        if (downloadManager is not null) surfaceControls.Remove(downloadManager);
        downloadManager = null; var overlay = downloadOverlay; downloadOverlay = null; overlay?.Dispose(); download.Focus();
    }
}
