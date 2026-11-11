using System.Drawing.Drawing2D;

namespace Pawchive;

public partial class MainForm
{
    sealed record AuthorSection(bool Active, int Count);
    readonly ModernButton expandAuthors = new() { Text = "↗", AccessibleName = "Расширить список авторов" };
    DataGridView? expandedAuthors;
    Panel? authorManager;
    CreatorBackdrop? authorOverlay;
    string? downloadingCreator;
    string? dropCreator;
    bool creatorRefreshPending;
    readonly Dictionary<string, int> creatorPostCounts = [];
    const string CreatorDragFormat = "Pawchive.CreatorOrder";

    void ConfigureCreatorGrid(DataGridView grid, bool detailed)
    {
        grid.Tag = detailed ? "creator-details" : "creator-sidebar";
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Выбрать", Width = 28, Resizable = DataGridViewTriState.False });
        grid.Columns.Add(new FastTextColumn { Name = "Author", HeaderText = "Автор", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        foreach (var name in new[] { "Service", "Id", "State" }) grid.Columns.Add(new FastTextColumn { Name = name, Visible = false });
        grid.Columns.Add(new FastTextColumn { Name = "Grip", HeaderText = "Порядок скачивания", Width = 30, ReadOnly = true });
        grid.Columns["Grip"]!.DisplayIndex = 0;
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Earlier", HeaderText = "Выше", Width = 38, ReadOnly = true });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Later", HeaderText = "Ниже", Width = 38, ReadOnly = true });
        grid.Columns.Add(new FastTextColumn { Name = "Inset", Width = 10, ReadOnly = true });
        foreach (DataGridViewColumn column in grid.Columns) { column.SortMode = DataGridViewColumnSortMode.NotSortable; column.Resizable = DataGridViewTriState.False; }
        grid.ColumnHeadersVisible = false; grid.RowTemplate.Height = detailed ? 100 : 106;
        grid.ReadOnly = true; grid.AllowDrop = true;
        grid.SelectionChanged += (_, _) => { if (grid.SelectedCells.Count > 0) grid.ClearSelection(); };
        grid.AccessibleName = "Авторы для проверки и скачивания";
        grid.CellPainting += PaintCreatorCell;
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if (loading || e.RowIndex < 0 || e.ColumnIndex != 0 || grid.Rows[e.RowIndex].Tag is not Creator c) return;
            SetCreatorSelected(c.Key, Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value));
        };
        grid.CellMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Rows[e.RowIndex].Tag is not Creator c) return;
            var name = grid.Columns[e.ColumnIndex].Name;
            if (name == "Selected") SetCreatorSelected(c.Key, !c.Selected);
            if (name is "Earlier" or "Later") MoveCreator(c.Key, name == "Earlier" ? -1 : 1);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && grid.CurrentRow?.Tag is Creator toggle)
            {
                var column = grid.CurrentCell?.OwningColumn?.Name;
                if (column is "Earlier" or "Later") MoveCreator(toggle.Key, column == "Earlier" ? -1 : 1);
                else SetCreatorSelected(toggle.Key, !toggle.Selected);
                e.Handled = e.SuppressKeyPress = true;
            }
            if (e.Alt && e.KeyCode is Keys.Up or Keys.Down && grid.CurrentRow?.Tag is Creator c)
            { MoveCreator(c.Key, e.KeyCode == Keys.Up ? -1 : 1); e.Handled = e.SuppressKeyPress = true; }
        };
        Point dragStart = Point.Empty; string? dragKey = null;
        grid.MouseDown += (_, e) =>
        {
            dragKey = null; var hit = grid.HitTest(e.X, e.Y);
            if (operation is not null || e.Button != MouseButtons.Left || hit.RowIndex < 0 || hit.ColumnIndex < 0) return;
            if (grid.Columns[hit.ColumnIndex].Name == "Grip" && grid.Rows[hit.RowIndex].Tag is Creator { Selected: true } c)
            { dragStart = e.Location; dragKey = c.Key; }
        };
        grid.MouseMove += (_, e) =>
        {
            if (dragKey is null || e.Button != MouseButtons.Left || operation is not null) return;
            if (Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height / 2) return;
            string key = dragKey; dragKey = null;
            var data = new DataObject(); data.SetData(CreatorDragFormat, key);
            try { grid.DoDragDrop(data, DragDropEffects.Move); }
            finally { dropCreator = null; grid.Invalidate(); }
        };
        long lastDragScroll = 0;
        grid.DragOver += (_, e) =>
        {
            var previousDrop = dropCreator;
            e.Effect = DragDropEffects.None; dropCreator = null;
            if (operation is not null || e.Data?.GetData(CreatorDragFormat) is not string key || !Selected().Any(c => c.Key == key)) return;
            var point = grid.PointToClient(new Point(e.X, e.Y)); var hit = grid.HitTest(point.X, point.Y);
            if (hit.RowIndex >= 0 && grid.Rows[hit.RowIndex].Tag is Creator { Selected: true } target)
            { dropCreator = target.Key; e.Effect = DragDropEffects.Move; }
            if (grid.Rows.Count > 0 && Environment.TickCount64 - lastDragScroll > 120 && (point.Y < 22 || point.Y > grid.Height - 22))
            {
                lastDragScroll = Environment.TickCount64;
                int first = grid.FirstDisplayedScrollingRowIndex;
                if (first >= 0) grid.FirstDisplayedScrollingRowIndex = Math.Clamp(first + (point.Y < 22 ? -1 : 1), 0, grid.Rows.Count - 1);
            }
            if (previousDrop != dropCreator) grid.Invalidate();
        };
        grid.DragLeave += (_, _) => { dropCreator = null; grid.Invalidate(); };
        grid.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(CreatorDragFormat) is string key && dropCreator is string target) MoveCreatorTo(key, target);
            dropCreator = null; grid.Invalidate();
        };
    }

    void FillCreatorGrid(DataGridView grid)
    {
        var focused = (grid.CurrentRow?.Tag as Creator)?.Key;
        int first = grid.FirstDisplayedScrollingRowIndex;
        grid.SuspendLayout(); grid.Rows.Clear();
        foreach (bool active in new[] { true, false })
        {
            var group = settings.Creators.Where(c => c.Selected == active).ToList();
            var section = grid.Rows[grid.Rows.Add()]; section.Tag = new AuthorSection(active, group.Count);
            section.ReadOnly = true; section.Height = (int)(34 * grid.DeviceDpi / 96f);
            section.Cells["Author"].Value = Locale.T(active ? "Активные" : "Неактивные") + " · " + group.Count;
            int order = 0;
            foreach (var c in group)
            {
                var row = grid.Rows[grid.Rows.Add(c.Selected, c.Name, c.Service, c.Id, catalogDates.GetValueOrDefault(c.Key, "Ещё не проверен"), active ? (++order).ToString() : "", "↑", "↓")];
                row.Tag = c;
                row.Cells["Author"].ToolTipText = c.Name + "\n" + c.Service + " · " + c.Id + "\n" + CreatorPostCount(creatorPostCounts.GetValueOrDefault(c.Key)) + "\n" + Locale.T(catalogDates.GetValueOrDefault(c.Key, "Ещё не проверен"));
                row.Cells["Earlier"].ToolTipText = Locale.T("Выше") + " (Alt+↑)"; row.Cells["Later"].ToolTipText = Locale.T("Ниже") + " (Alt+↓)";
            }
        }
        foreach (DataGridViewRow row in grid.Rows)
            if (row.Tag is Creator c && c.Key == focused) { grid.CurrentCell = row.Cells["Author"]; break; }
        if (first >= 0 && grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = Math.Min(first, grid.Rows.Count - 1);
        grid.ClearSelection(); grid.ResumeLayout(); grid.Invalidate();
    }
    void RefreshCreatorViews(bool animate)
    {
        creatorPostCounts.Clear(); foreach (var group in posts.GroupBy(p => p.Creator.Key)) creatorPostCounts[group.Key] = group.Count();
        var grids = new[] { authors, expandedAuthors }.OfType<DataGridView>().Where(g => !g.IsDisposed).ToArray();
        var positions = animate ? grids.ToDictionary(g => g, CreatorMotion.Positions) : null;
        loading = true;
        try { foreach (var grid in grids) FillCreatorGrid(grid); }
        finally { loading = false; }
        if (positions is not null) foreach (var grid in grids) CreatorMotion.Play(grid, positions[grid]);
    }
    void ScheduleCreatorViews()
    {
        if (creatorRefreshPending || !IsHandleCreated || IsDisposed) return;
        creatorRefreshPending = true;
        BeginInvoke(() => { creatorRefreshPending = false; if (!IsDisposed) RefreshCreatorViews(true); });
    }
    void SetCreatorSelected(string key, bool selected)
    {
        if (operation is not null) return;
        var creator = settings.Creators.FirstOrDefault(c => c.Key == key);
        if (creator is null || creator.Selected == selected) return;
        settings.Creators.Remove(creator);
        if (selected) settings.Creators.Insert(settings.Creators.Count(c => c.Selected), creator with { Selected = true });
        else settings.Creators.Add(creator with { Selected = false });
        // Stable partition also handles settings written by older versions.
        settings.Creators = settings.Creators.OrderByDescending(c => c.Selected).ToList();
        SaveSettings(); currentPage = 0; FillPosts(); ScheduleCreatorViews(); UpdateCreatorBackdrop();
    }
    void MoveCreator(string key, int delta)
    {
        var selected = Selected(); int from = selected.FindIndex(c => c.Key == key), to = from + delta;
        if (from < 0 || to < 0 || to >= selected.Count) return;
        MoveCreatorTo(key, selected[to].Key);
    }
    void MoveCreatorTo(string key, string target)
    {
        if (operation is not null || key == target) return;
        var selected = Selected(); int from = selected.FindIndex(c => c.Key == key), to = selected.FindIndex(c => c.Key == target);
        if (from < 0 || to < 0) return;
        var creator = selected[from]; selected.RemoveAt(from); selected.Insert(to, creator);
        settings.Creators = selected.Concat(settings.Creators.Where(c => !c.Selected)).ToList();
        SaveSettings(); currentPage = 0; FillPosts(); RefreshCreatorViews(true); UpdateCreatorBackdrop();
    }
    void SetDownloadingCreator(string? key)
    {
        downloadingCreator = key;
        foreach (var grid in new[] { authors, expandedAuthors }.OfType<DataGridView>())
        {
            foreach (var motion in grid.Controls.OfType<CreatorMotion>().ToArray()) motion.Dispose();
            if (key is not null && grid.IsHandleCreated)
                foreach (DataGridViewRow row in grid.Rows)
                    if (row.Tag is Creator c && c.Key == key && !row.Displayed) { grid.FirstDisplayedScrollingRowIndex = row.Index; break; }
            grid.Invalidate(); grid.Update();
        }
        UpdateCreatorBackdrop();
    }
    async Task WithCreatorHighlight(Post post, Func<Task> action)
    {
        SetDownloadingCreator(post.Creator.Key);
        try { await action(); }
        finally { SetDownloadingCreator(null); }
    }
    IEnumerable<Post> OrderedPosts(IEnumerable<Post> source)
    {
        var order = settings.Creators.Select((c, i) => (c.Key, i)).ToDictionary(c => c.Key, c => c.i);
        return source.OrderBy(p => order.GetValueOrDefault(p.Creator.Key, int.MaxValue))
            .ThenBy(p => p.Creator.Key, StringComparer.Ordinal).ThenByDescending(p => p.Published, StringComparer.Ordinal);
    }
    static string CreatorPostCount(int count) => Locale.T($"Постов: {count:N0}");
    void ShowAuthorManager()
    {
        if (authorManager is not null) { authorManager.Focus(); return; }
        var snapshot = new Bitmap(ClientSize.Width, ClientSize.Height);
        var shell = Controls.Cast<Control>().First(c => c.Dock == DockStyle.Fill);
        shell.DrawToBitmap(snapshot, new Rectangle(Point.Empty, ClientSize));
        var overlay = new CreatorBackdrop(snapshot) { Dock = DockStyle.Fill };
        var card = new CreatorManagerCard { Text = Locale.T("Авторы и порядок скачивания"), LineColor = palette.Border };
        Surface(card);
        authorOverlay = overlay; authorManager = card;
        var layout = Compose(1, 4); layout.Padding = new Padding(22);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var heading = Compose(2, 1); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        heading.Controls.Add(new Label { Text = "Авторы и порядок скачивания", Font = new Font(Font.FontFamily, 14), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var close = new ModernButton { Text = "×", AccessibleName = "Закрыть", Dock = DockStyle.Fill, Margin = Padding.Empty };
        close.Click += (_, _) => CloseAuthorManager(); heading.Controls.Add(close, 1, 0); layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new Label { Text = "Перетаскивайте активных авторов или используйте стрелки. Изменения применяются сразу.", Dock = DockStyle.Fill, Tag = "muted" }, 0, 1);
        var grid = Grid(); expandedAuthors = grid; ConfigureCreatorGrid(grid, true);
        layout.Controls.Add(new GridHost(grid) { Dock = DockStyle.Fill }, 0, 2);
        var done = new ModernButton { Text = "Готово", Primary = true, Dock = DockStyle.Right, Width = 130, Margin = new Padding(0, 12, 0, 0) };
        done.Click += (_, _) => CloseAuthorManager(); layout.Controls.Add(done, 0, 3);
        card.Controls.Add(layout); overlay.Controls.Add(card); Controls.Add(overlay); overlay.BringToFront();
        void Arrange()
        {
            float scale = DeviceDpi / 96f;
            card.Size = new Size(Math.Min((int)(840 * scale), Math.Max(1, overlay.ClientSize.Width - 48)), Math.Min((int)(680 * scale), Math.Max(1, overlay.ClientSize.Height - 48)));
            card.Location = new Point((overlay.ClientSize.Width - card.Width) / 2, (overlay.ClientSize.Height - card.Height) / 2);
        }
        overlay.Resize += (_, _) => Arrange();
        overlay.MouseDown += (_, _) => CloseAuthorManager();
        KeyPreview = true;
        Arrange(); RefreshCreatorViews(false); grid.Enabled = operation is null;
        ThemeTree(card); UiLanguage.Bind(card); close.Focus();
    }
    void CloseAuthorManager()
    {
        var overlay = authorOverlay;
        if (authorManager is not null) surfaceControls.Remove(authorManager);
        authorOverlay = null; authorManager = null; expandedAuthors = null;
        overlay?.Dispose(); expandAuthors.Focus();
    }
    void UpdateCreatorBackdrop()
    {
        if (authorOverlay is null || authorOverlay.IsDisposed || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        var snapshot = new Bitmap(ClientSize.Width, ClientSize.Height);
        Controls.Cast<Control>().First(c => c.Dock == DockStyle.Fill && c != authorOverlay).DrawToBitmap(snapshot, new Rectangle(Point.Empty, ClientSize));
        authorOverlay.UpdateSnapshot(snapshot);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (authorManager is not null)
        {
            if (keyData == Keys.Escape) { CloseAuthorManager(); return true; }
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                authorManager.SelectNextControl(FocusedCreatorControl(authorManager), keyData == Keys.Tab, true, true, true);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    static Control? FocusedCreatorControl(Control parent)
    {
        foreach (Control child in parent.Controls)
        { if (child.Focused) return child; if (child.ContainsFocus) return FocusedCreatorControl(child); }
        return null;
    }
    void PaintCreatorCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || sender is not DataGridView grid) return;
        var row = grid.Rows[e.RowIndex]; var g = e.Graphics!; var r = e.CellBounds; float s = grid.DeviceDpi / 96f;
        bool current = row.Tag is Creator c0 && c0.Key == downloadingCreator;
        Color bg = current ? palette.Selection : row.Tag is Creator { Selected: true } ? palette.Surface : palette.Background;
        using var fill = new SolidBrush(bg);
        using (var background = new SolidBrush(palette.Background)) g.FillRectangle(background, r);
        if (row.Tag is Creator)
        {
            var clip = g.Save(); g.SetClip(r, CombineMode.Intersect);
            var card = grid.GetRowDisplayRectangle(e.RowIndex, false); card.Inflate(-2, -(int)(3 * s));
            using var rounded = IconDrawing.Rounded(card, (int)(6 * s)); g.FillPath(fill, rounded);
            if (current || row.Tag is Creator { Selected: true }) { using var border = new Pen(current ? palette.AccentInk : palette.Border); g.DrawPath(border, rounded); }
            g.Restore(clip);
        }
        var name = grid.Columns[e.ColumnIndex].Name;
        using var small = new Font(Font.FontFamily, 8.5f); using var title = new Font("Segoe UI Semibold", Font.Size);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (row.Tag is AuthorSection section)
        {
            if (name == "Author") TextRenderer.DrawText(g, Locale.T(section.Active ? "Активные" : "Неактивные") + " · " + section.Count, small, r, palette.Muted, flags);
            e.Handled = true; return;
        }
        if (row.Tag is not Creator creator) { e.Handled = true; return; }
        if (name == "Selected")
        {
            int checkboxTop = r.Y + (r.Height - (int)((grid == expandedAuthors ? 40 : 80) * s)) / 2 + (int)(2 * s);
            var box = new Rectangle(r.X + (int)(5 * s), checkboxTop, (int)(15 * s), (int)(15 * s));
            using var brush = new SolidBrush(creator.Selected ? palette.Accent : palette.Surface); using var pen = new Pen(creator.Selected ? palette.Accent : palette.Muted);
            using var path = IconDrawing.Rounded(box, (int)(3 * s)); g.FillPath(brush, path); g.DrawPath(pen, path);
            if (creator.Selected) { using var tick = new Pen(palette.AccentText, 1.8f * s); g.DrawLines(tick, [new PointF(box.X + 3 * s, box.Y + 7 * s), new(box.X + 6 * s, box.Y + 10 * s), new(box.X + 12 * s, box.Y + 4 * s)]); }
        }
        else if (name == "Grip")
        { if (creator.Selected) TextRenderer.DrawText(g, "⠿", title, r, palette.Muted, flags | TextFormatFlags.HorizontalCenter); }
        else if (name is "Earlier" or "Later")
        {
            if (creator.Selected)
            {
                var selected = Selected(); int index = selected.FindIndex(c => c.Key == creator.Key);
                bool enabled = operation is null && (name == "Earlier" ? index > 0 : index < selected.Count - 1);
                var box = new Rectangle(r.X + (r.Width - (int)(28 * s)) / 2, r.Y + (r.Height - (int)(28 * s)) / 2, (int)(28 * s), (int)(28 * s));
                using var outline = new Pen(palette.Border); using var rounded = IconDrawing.Rounded(box, (int)(5 * s)); g.DrawPath(outline, rounded);
                float cx = box.Left + box.Width / 2f, cy = box.Top + box.Height / 2f, direction = name == "Earlier" ? -1 : 1;
                using var arrow = new Pen(enabled ? palette.Text : AppPalette.Blend(palette.Muted, bg, .55), 1.4f * s);
                g.DrawLine(arrow, cx, cy - 5 * s, cx, cy + 5 * s);
                g.DrawLines(arrow, [new PointF(cx - 3 * s, cy + direction * 2 * s), new(cx, cy + direction * 5 * s), new(cx + 3 * s, cy + direction * 2 * s)]);
            }
        }
        else if (name == "Author")
        {
            int count = creatorPostCounts.GetValueOrDefault(creator.Key), rank = Selected().FindIndex(c => c.Key == creator.Key) + 1;
            string heading = (creator.Selected ? rank + ". " : "") + creator.Name;
            var text = Rectangle.Inflate(r, -(int)(4 * s), 0);
            bool detailed = grid == expandedAuthors;
            int line = (int)(20 * s), top = r.Y + (r.Height - (detailed ? 2 : 4) * line) / 2;
            int textWidth = detailed ? text.Width / 2 : text.Width;
            var titleRect = new Rectangle(text.X, top, textWidth, line);
            TextRenderer.DrawText(g, heading, title, titleRect, AppPalette.Readable(palette.Text, bg), flags);
            TextRenderer.DrawText(g, creator.Service + " · " + creator.Id, small, new Rectangle(text.X, top + line, textWidth, line), AppPalette.Readable(palette.Muted, bg), flags);
            if (!detailed) TextRenderer.DrawText(g, CreatorPostCount(count), small, new Rectangle(text.X, top + 2 * line, text.Width, line), AppPalette.Readable(palette.Text, bg), flags);
            string state = Convert.ToString(row.Cells["State"].Value) ?? "";
            if (state.Contains(" • ")) state = state[(state.LastIndexOf(" • ", StringComparison.Ordinal) + 3)..];
            if (detailed)
            {
                int width = text.Width / 2;
                TextRenderer.DrawText(g, CreatorPostCount(count), Font, new Rectangle(text.X + width, top, text.Width - width, line), AppPalette.Readable(palette.Text, bg), flags);
                TextRenderer.DrawText(g, Locale.Error(state), small, new Rectangle(text.X + width, top + line, text.Width - width, line), AppPalette.Readable(palette.Muted, bg), flags);
            }
            else TextRenderer.DrawText(g, state.StartsWith("Ошибка: ") ? Locale.Error(state) : Locale.T(state), small, new Rectangle(text.X, top + 3 * line, text.Width, line), AppPalette.Readable(palette.Muted, bg), flags);
        }
        if (dropCreator == creator.Key) { using var pen = new Pen(palette.AccentInk, 2); g.DrawLine(pen, r.Left, r.Bottom - 2, r.Right, r.Bottom - 2); }
        e.Handled = true;
    }
}

// Short row movement overlays; the live grid remains the sole source of input and accessibility.
sealed class CreatorMotion : Control
{
    readonly Bitmap image;
    readonly List<(Bitmap Tile, Rectangle From, Rectangle To)> tiles = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly long started = Environment.TickCount64;
    public static Dictionary<string, Rectangle> Positions(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
        .Where(r => r.Tag is Creator && r.Displayed).ToDictionary(r => ((Creator)r.Tag!).Key, r => grid.GetRowDisplayRectangle(r.Index, false));
    public static void Play(DataGridView grid, Dictionary<string, Rectangle> before)
    {
        if (!grid.Visible || !grid.IsHandleCreated || grid.Width < 1 || grid.Height < 1 || SystemInformation.HighContrast || !SystemInformation.IsMenuAnimationEnabled) return;
        foreach (var old in grid.Controls.OfType<CreatorMotion>().ToArray()) old.Dispose();
        var after = Positions(grid);
        if (!after.Any(p => before.TryGetValue(p.Key, out var r) && r.Y != p.Value.Y)) return;
        var motion = new CreatorMotion(grid, before, after); grid.Controls.Add(motion); motion.BringToFront(); motion.timer.Start();
    }
    CreatorMotion(DataGridView grid, Dictionary<string, Rectangle> before, Dictionary<string, Rectangle> after)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        Bounds = grid.ClientRectangle; Enabled = false; BackColor = grid.BackgroundColor; TabStop = false;
        image = new Bitmap(Width, Height); grid.DrawToBitmap(image, grid.ClientRectangle);
        foreach (var (key, to) in after)
        {
            if (!before.TryGetValue(key, out var from) || from.Y == to.Y) continue;
            var crop = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size), to);
            if (crop.Width > 0 && crop.Height > 0) tiles.Add((image.Clone(crop, image.PixelFormat), new Rectangle(crop.X, from.Y + crop.Y - to.Y, crop.Width, crop.Height), crop));
        }
        timer.Tick += (_, _) => { if (Environment.TickCount64 - started >= 190) Dispose(); else Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(image, Point.Empty);
        using var brush = new SolidBrush(BackColor); foreach (var tile in tiles) e.Graphics.FillRectangle(brush, tile.To);
        float t = Math.Clamp((Environment.TickCount64 - started) / 190f, 0, 1); t = 1 - (1 - t) * (1 - t);
        foreach (var tile in tiles) e.Graphics.DrawImageUnscaled(tile.Tile, tile.To.X, (int)(tile.From.Y + (tile.To.Y - tile.From.Y) * t));
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); image.Dispose(); foreach (var tile in tiles) tile.Tile.Dispose(); } base.Dispose(disposing); }
}

sealed class CreatorBackdrop : Panel
{
    Bitmap snapshot;
    public CreatorBackdrop(Bitmap snapshot) { this.snapshot = snapshot; DoubleBuffered = true; }
    public void UpdateSnapshot(Bitmap next) { var old = snapshot; snapshot = next; old.Dispose(); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.DrawImage(snapshot, ClientRectangle);
        using var dim = new SolidBrush(Color.FromArgb(155, Color.Black));
        e.Graphics.FillRectangle(dim, ClientRectangle);
    }
    protected override void Dispose(bool disposing) { if (disposing) snapshot.Dispose(); base.Dispose(disposing); }
}

sealed class CreatorManagerCard : SurfacePanel
{
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var path = IconDrawing.Rounded(new Rectangle(0, 0, Width, Height), (int)(12 * DeviceDpi / 96f));
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = IconDrawing.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)(12 * DeviceDpi / 96f));
        using var pen = new Pen(LineColor); e.Graphics.DrawPath(pen, path);
    }
}
