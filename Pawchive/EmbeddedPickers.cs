using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
namespace Pawchive;

public partial class MainForm
{
    async Task<string?> ChooseLibraryFolder(string initial)
    {
        using var dialog = Dialog("Папка загрузок и истории", new Size(800, 500));
        var layout = Compose(1, 5); layout.Padding = new Padding(22, 16, 22, 20);
        foreach (int h in new[] { 28, 46, 0, 30, 42 }) layout.RowStyles.Add(new RowStyle(h == 0 ? SizeType.Percent : SizeType.Absolute, h == 0 ? 100 : h));
        layout.Controls.Add(new Label { Text = "Откройте папку двойным щелчком или введите путь.", Dock = DockStyle.Fill, Tag = "muted", Margin = Padding.Empty }, 0, 0);
        var navigation = Compose(4, 1); navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var up = new ModernButton { Glyph = AppIcon.Left, AccessibleName = "На уровень выше", Dock = DockStyle.Top, Width = 38, Enabled = false };
        var drives = new ModernCombo { Dock = DockStyle.Top, Margin = new Padding(0, 0, 8, 0), AccessibleName = "Диск" };
        foreach (var drive in DriveInfo.GetDrives()) drives.Items.Add(drive.Name);
        var path = new TextBox { Text = initial, AccessibleName = "Путь к папке" };
        var frame = new TextFieldFrame(path) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 8, 0) };
        var go = new ModernButton { Text = "Открыть", Dock = DockStyle.Top, Margin = Padding.Empty };
        navigation.Controls.Add(up, 0, 0); navigation.Controls.Add(drives, 1, 0); navigation.Controls.Add(frame, 2, 0); navigation.Controls.Add(go, 3, 0); layout.Controls.Add(navigation, 0, 1);
        var grid = Grid(); grid.ColumnHeadersVisible = false; grid.ReadOnly = true; grid.AllowUserToResizeColumns = false;
        grid.RowTemplate.Height = 36; grid.Columns.Add(new FastTextColumn { Name = "Folder", HeaderText = "Папка", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(new GridHost(grid) { Dock = DockStyle.Fill, Outline = true, Padding = new Padding(1) }, 0, 2);
        var note = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted", Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }; layout.Controls.Add(note, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var accept = new ModernButton { Text = "Выбрать папку", Primary = true, Width = 160, Height = 40 }; var cancel = new ModernButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 112, Height = 40 };
        actions.Controls.AddRange([accept, cancel]); layout.Controls.Add(actions, 0, 4); dialog.Controls.Add(layout); dialog.AcceptButton = accept; dialog.CancelButton = cancel; dialog.InitialFocus = path;
        string current = "", selected = ""; int generation = 0; bool changingDrive = false;
        async Task Navigate(string target)
        {
            int request = ++generation;
            try
            {
                target = Path.GetFullPath(target.Trim()); path.Text = target; note.Text = "Открываю папки…"; note.ForeColor = palette.Muted; go.Enabled = false;
                var folders = await Task.Run(() => Directory.EnumerateDirectories(target).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToArray());
                if (dialog.IsDisposed || request != generation) return;
                current = target; grid.Rows.Clear();
                foreach (var folder in folders) { int row = grid.Rows.Add(Path.GetFileName(folder)); grid.Rows[row].Tag = folder; grid.Rows[row].Cells[0].ToolTipText = folder; }
                grid.ClearSelection(); note.Text = folders.Length == 0 ? "В этой папке нет вложенных папок." : "Путь можно вставить вручную. Несуществующая папка будет создана при выборе.";
                up.Enabled = Directory.GetParent(current) is not null;
                changingDrive = true; drives.SelectedIndex = drives.Items.IndexOf(Path.GetPathRoot(current)); changingDrive = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { if (!dialog.IsDisposed && request == generation) { UiLanguage.SetError(note, e.Message); note.ForeColor = palette.Error; } }
            finally { if (!dialog.IsDisposed && request == generation) go.Enabled = true; }
        }
        go.Click += async (_, _) => await Navigate(path.Text);
        path.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; await Navigate(path.Text); } };
        dialog.KeyHandler = key => { if (key == Keys.Enter && path.Focused) { go.PerformClick(); return true; } return false; };
        up.Click += async (_, _) => { if (Directory.GetParent(current) is DirectoryInfo parent) await Navigate(parent.FullName); };
        drives.SelectedIndexChanged += async (_, _) => { if (!changingDrive && drives.SelectedItem is string drive) await Navigate(drive); };
        grid.CellClick += (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is string folder) path.Text = folder; };
        grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is string folder) await Navigate(folder); };
        accept.Click += async (_, _) =>
        {
            accept.Enabled = false;
            try
            {
                if (string.IsNullOrWhiteSpace(path.Text)) throw new ArgumentException("Укажите путь к папке.");
                string chosen = Path.GetFullPath(path.Text.Trim());
                await Task.Run(() => Directory.CreateDirectory(chosen));
                if (!dialog.IsDisposed && embeddedDialog == dialog) { selected = chosen; dialog.DialogResult = DialogResult.OK; }
            }
            catch (Exception e) { if (!dialog.IsDisposed) { UiLanguage.SetError(note, e.Message); note.ForeColor = palette.Error; } }
            finally { if (!dialog.IsDisposed) accept.Enabled = true; }
        };
        async Task Start()
        {
            int initialGeneration = generation;
            string start = await Task.Run(() =>
            {
                string existing = initial;
                try { while (!Directory.Exists(existing) && Directory.GetParent(existing) is DirectoryInfo parent) existing = parent.FullName; }
                catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { existing = ""; }
                return Directory.Exists(existing) ? existing : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            });
            if (!dialog.IsDisposed && generation == initialGeneration) await Navigate(start);
        }
        var result = ShowEmbeddedDialog(dialog); _ = Start();
        if (await result != DialogResult.OK) return null;
        return selected;
    }

    async Task<string?> ChooseAccentColor(string initial)
    {
        using var dialog = Dialog("Выбрать цвет…", new Size(620, 430));
        var layout = Compose(1, 4); layout.Padding = new Padding(22, 18, 22, 20);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var spectrumRow = Compose(2, 1); spectrumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); spectrumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        var spectrum = new ColorSpectrum { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 12), AccessibleName = "Насыщенность и яркость" };
        var hue = new HueStrip { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), AccessibleName = "Оттенок" };
        spectrumRow.Controls.Add(spectrum, 0, 0); spectrumRow.Controls.Add(hue, 1, 0); layout.Controls.Add(spectrumRow, 0, 0);
        var valueRow = Compose(2, 2); valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        valueRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); valueRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var preview = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Tag = "color-swatch" }; valueRow.Controls.Add(preview, 0, 0); valueRow.SetRowSpan(preview, 2);
        valueRow.Controls.Add(new Label { Text = "Цвет в формате #RRGGBB", AutoSize = true, Margin = Padding.Empty }, 1, 0);
        var hex = new TextBox { Text = initial, AccessibleName = "Цвет в формате #RRGGBB" }; valueRow.Controls.Add(new TextFieldFrame(hex) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 1); layout.Controls.Add(valueRow, 0, 1);
        var error = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty }; layout.Controls.Add(error, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var save = new ModernButton { Text = "Выбрать цвет", Primary = true, Width = 160, Height = 40 }; var cancel = new ModernButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 112, Height = 40 };
        actions.Controls.AddRange([save, cancel]); layout.Controls.Add(actions, 0, 3); dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.InitialFocus = hex;
        bool changing = false; string selected = initial;
        bool Parse(string text, out Color color)
        {
            color = Color.Empty; text = text.Trim(); if (text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); return true;
        }
        void Preview(Color color) { preview.BackColor = color; }
        void SetColor(Color color)
        {
            changing = true; hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; changing = false; Preview(color);
        }
        spectrum.Changed += (_, _) => SetColor(spectrum.SelectedColor);
        hue.Changed += (_, _) => { spectrum.Hue = hue.Hue; SetColor(spectrum.SelectedColor); };
        hex.TextChanged += (_, _) => { if (Parse(hex.Text, out var color)) { error.Text = ""; if (!changing) { spectrum.SetColor(color); hue.Hue = spectrum.Hue; Preview(color); } } };
        save.Click += (_, _) =>
        {
            if (!Parse(hex.Text, out var color)) { error.Text = "Укажите цвет в формате #RRGGBB."; error.ForeColor = palette.Error; return; }
            selected = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; dialog.DialogResult = DialogResult.OK;
        };
        Parse(initial, out var first); spectrum.SetColor(first); hue.Hue = spectrum.Hue;
        var result = ShowEmbeddedDialog(dialog); Preview(first);
        return await result == DialogResult.OK ? selected : null;
    }
}

public sealed class ColorSpectrum : Control
{
    double hue, saturation, value;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Hue { get => hue; set { hue = Math.Clamp(value, 0, 360); Invalidate(); } }
    public Color SelectedColor => Hsv(hue, saturation, value);
    public event EventHandler? Changed;
    public ColorSpectrum() { DoubleBuffered = true; Cursor = Cursors.Cross; TabStop = true; AccessibleRole = AccessibleRole.Slider; }
    public void SetColor(Color color) { hue = color.GetHue(); saturation = color.GetBrightness() == 0 ? 0 : 1 - Math.Min(color.R, Math.Min(color.G, color.B)) / (double)Math.Max(1, (int)Math.Max(color.R, Math.Max(color.G, color.B))); value = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d; Invalidate(); }
    public static Color Hsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation, x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - chroma;
        (double r, double g, double b) = hue switch { < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x), < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        using var horizontal = new LinearGradientBrush(ClientRectangle, Color.White, Hsv(hue, 1, 1), 0f); e.Graphics.FillRectangle(horizontal, ClientRectangle);
        using var vertical = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.Black, 90f); e.Graphics.FillRectangle(vertical, ClientRectangle);
        float x = (float)(saturation * (Width - 1)), y = (float)((1 - value) * (Height - 1));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var dark = new Pen(Color.Black, 3); using var light = new Pen(Color.White, 1); e.Graphics.DrawEllipse(dark, x - 5, y - 5, 10, 10); e.Graphics.DrawEllipse(light, x - 5, y - 5, 10, 10);
    }
    void Pick(Point position) { saturation = Math.Clamp(position.X / (double)Math.Max(1, Width - 1), 0, 1); value = 1 - Math.Clamp(position.Y / (double)Math.Max(1, Height - 1), 0, 1); Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; Pick(e.Location); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Pick(e.Location); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down)) return; saturation = Math.Clamp(saturation + (e.KeyCode == Keys.Left ? -.01 : e.KeyCode == Keys.Right ? .01 : 0), 0, 1); value = Math.Clamp(value + (e.KeyCode == Keys.Down ? -.01 : e.KeyCode == Keys.Up ? .01 : 0), 0, 1); e.Handled = true; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
}
public sealed class HueStrip : Control
{
    double hue;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Hue { get => hue; set { hue = Math.Clamp(value, 0, 360); Invalidate(); } }
    public event EventHandler? Changed;
    public HueStrip() { DoubleBuffered = true; Cursor = Cursors.Cross; TabStop = true; AccessibleRole = AccessibleRole.Slider; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        using var brush = new LinearGradientBrush(ClientRectangle, Color.Red, Color.Red, 90f) { InterpolationColors = new ColorBlend { Colors = [Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red], Positions = [0, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1] } };
        e.Graphics.FillRectangle(brush, ClientRectangle); int y = (int)(hue / 360 * (Height - 1)); using var dark = new Pen(Color.Black, 3); using var light = new Pen(Color.White); e.Graphics.DrawLine(dark, 0, y, Width, y); e.Graphics.DrawLine(light, 0, y, Width, y);
    }
    void Pick(int y) { Hue = Math.Clamp(y / (double)Math.Max(1, Height - 1), 0, 1) * 360; Changed?.Invoke(this, EventArgs.Empty); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; Pick(e.Y); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Pick(e.Y); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is not (Keys.Up or Keys.Down)) return; Hue += e.KeyCode == Keys.Up ? -1 : 1; e.Handled = true; Changed?.Invoke(this, EventArgs.Empty); }
}
