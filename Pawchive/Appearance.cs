using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.ComponentModel;

namespace Pawchive;

public sealed record AppPalette(Color Background, Color Surface, Color Soft, Color Text, Color Muted,
    Color Border, Color Accent, Color AccentText, Color AccentInk, Color Selection, Color Good, Color Error, Color Warning, bool Dark)
{
    public static Color Blend(Color a, Color b, double amount) => Color.FromArgb(
        (int)Math.Round(a.R * (1 - amount) + b.R * amount),
        (int)Math.Round(a.G * (1 - amount) + b.G * amount),
        (int)Math.Round(a.B * (1 - amount) + b.B * amount));
    public static double Luminance(Color color)
    {
        static double Linear(byte v) { var n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    public static double Contrast(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    public static Color Readable(Color color, Color background, double ratio = 4.5)
    {
        var target = Luminance(background) > .35 ? Color.Black : Color.White;
        for (var step = 0; step <= 20; step++) { var candidate = Blend(color, target, step / 20.0); if (Contrast(candidate, background) >= ratio) return candidate; }
        return target;
    }
    public static AppPalette Create(bool dark, Color accent, bool highContrast = false)
    {
        if (highContrast) return new(SystemColors.Window, SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText,
            SystemColors.HotTrack, SystemColors.Highlight, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, dark);
        var bg = dark ? Color.FromArgb(23, 26, 33) : Color.FromArgb(247, 248, 250);
        var surface = dark ? Color.FromArgb(31, 36, 45) : Color.White;
        return new(bg, surface, dark ? Color.FromArgb(41, 47, 58) : Color.FromArgb(240, 242, 246),
            dark ? Color.FromArgb(233, 237, 244) : Color.FromArgb(31, 41, 55),
            dark ? Color.FromArgb(166, 178, 194) : Color.FromArgb(96, 109, 128),
            dark ? Color.FromArgb(58, 66, 80) : Color.FromArgb(217, 223, 232), accent,
            Contrast(accent, Color.White) >= Contrast(accent, Color.Black) ? Color.White : Color.Black,
            Readable(accent, surface), Blend(surface, accent, dark ? .25 : .13),
            dark ? Color.FromArgb(137, 208, 175) : Color.FromArgb(36, 113, 82),
            dark ? Color.FromArgb(242, 158, 169) : Color.FromArgb(173, 55, 68),
            dark ? Color.FromArgb(235, 196, 131) : Color.FromArgb(141, 94, 18), dark);
    }
}

static class WindowsAppearance
{
    [DllImport("dwmapi.dll")] static extern int DwmGetColorizationColor(out uint color, out bool opaque);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    public static AppPalette Read(Settings settings)
    {
        var dark = false;
        var accent = Color.FromArgb(125, 86, 166);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            if (DwmGetColorizationColor(out var argb, out _) == 0) accent = Color.FromArgb((int)((argb >> 16) & 255), (int)((argb >> 8) & 255), (int)(argb & 255));
            // The DWM accent DWORD uses ABGR, unlike DwmGetColorizationColor's ARGB.
            using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("AccentColor") is int raw)
            { var abgr = unchecked((uint)raw); accent = Color.FromArgb((int)(abgr & 255), (int)((abgr >> 8) & 255), (int)((abgr >> 16) & 255)); }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException or DllNotFoundException or EntryPointNotFoundException) { }
        dark = settings.Theme switch { "light" => false, "dark" => true, _ => dark };
        if (settings.AccentMode == "custom" && settings.AccentColor is { Length: 7 } hex && hex[0] == '#' && int.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            accent = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        return AppPalette.Create(dark, accent, SystemInformation.HighContrast);
    }
    public static void TitleBar(Form form, AppPalette palette)
    {
        if (!form.IsHandleCreated) return;
        try { int dark = palette.Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

public enum AppIcon { None, Archive, Folder, Settings, Plus, Trash, Refresh, Scan, Download, Stop, Link, History, Left, Right, Image, Video, File }

static class BrandAssets
{
    static readonly Lazy<Icon> icon = new(() =>
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("Pawchive.Brand.ico")!;
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    });
    static readonly Lazy<Bitmap> image = new(() =>
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("Pawchive.Brand.png")!;
        using var source = System.Drawing.Image.FromStream(stream);
        return new Bitmap(source);
    });
    public static Icon Icon => icon.Value;
    public static Bitmap Image => image.Value;
}

public class BrandView : Control
{
    public BrandView() { DoubleBuffered = true; TabStop = false; AccessibleName = "Pawchive"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); int side = Math.Min(Width, Height);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(BrandAssets.Image, new Rectangle((Width - side) / 2, (Height - side) / 2, side, side));
    }
}

public class TextFieldFrame : Panel
{
    readonly TextBox input;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    public TextFieldFrame(TextBox input)
    {
        this.input = input; DoubleBuffered = true; TabStop = false; Height = 38;
        input.BorderStyle = BorderStyle.None; input.Dock = DockStyle.None; Controls.Add(input);
        input.Enter += (_, _) => Invalidate(); input.Leave += (_, _) => Invalidate();
        Click += (_, _) => input.Focus();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (input is null) return;
        int inset = (int)(10 * DeviceDpi / 96f), height = input.PreferredHeight;
        input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(1, Width - inset * 2), height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.Gray); e.Graphics.Clear(Parent?.BackColor ?? p.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = IconDrawing.Rounded(new(0, 0, Width - 1, Height - 1), (int)(6 * DeviceDpi / 96f));
        using var fill = new SolidBrush(p.Surface); using var pen = new Pen(ContainsFocus ? p.AccentInk : p.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
    }
}

static class IconDrawing
{
    public static AppIcon FileType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" => AppIcon.Archive,
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".avif" or ".tif" or ".tiff" => AppIcon.Image,
        ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".m4v" => AppIcon.Video,
        _ => AppIcon.File
    };
    public static void Draw(Graphics g, AppIcon icon, Rectangle bounds, Color color)
    {
        if (icon == AppIcon.None) return;
        var state = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        void Line(float a, float b, float c, float d) => g.DrawLine(pen, a, b, c, d);
        switch (icon)
        {
            case AppIcon.Plus: Line(10, 4, 10, 16); Line(4, 10, 16, 10); break;
            case AppIcon.Stop: g.DrawRectangle(pen, 5, 5, 10, 10); break;
            case AppIcon.Left: g.DrawLines(pen, [new(12, 4), new(6, 10), new(12, 16)]); break;
            case AppIcon.Right: g.DrawLines(pen, [new(8, 4), new(14, 10), new(8, 16)]); break;
            case AppIcon.Folder: g.DrawPolygon(pen, [new(2, 5), new(8, 5), new(10, 7), new(18, 7), new(17, 16), new(2, 16)]); break;
            case AppIcon.Download: Line(10, 2, 10, 12); g.DrawLines(pen, [new(6, 8), new(10, 12), new(14, 8)]); g.DrawLines(pen, [new(3, 12), new(3, 17), new(17, 17), new(17, 12)]); break;
            case AppIcon.Refresh: g.DrawArc(pen, 3, 3, 14, 14, 35, 285); g.DrawLines(pen, [new(12, 3), new(17, 3), new(17, 8)]); break;
            case AppIcon.Scan: g.DrawLines(pen, [new(7, 3), new(3, 3), new(3, 7)]); g.DrawLines(pen, [new(13, 3), new(17, 3), new(17, 7)]); g.DrawLines(pen, [new(3, 13), new(3, 17), new(7, 17)]); g.DrawLines(pen, [new(13, 17), new(17, 17), new(17, 13)]); Line(6, 10, 14, 10); break;
            case AppIcon.Settings: g.DrawEllipse(pen, 5, 5, 10, 10); g.DrawEllipse(pen, 8, 8, 4, 4); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(10 + (float)Math.Cos(a) * 6, 10 + (float)Math.Sin(a) * 6, 10 + (float)Math.Cos(a) * 8, 10 + (float)Math.Sin(a) * 8); } break;
            case AppIcon.History: g.DrawArc(pen, 3, 3, 14, 14, 205, 300); g.DrawLines(pen, [new(2, 3), new(2, 8), new(7, 8)]); Line(10, 6, 10, 10); Line(10, 10, 13, 12); break;
            case AppIcon.Link: g.DrawArc(pen, 3, 8, 10, 8, 55, 265); g.DrawArc(pen, 7, 3, 10, 8, 235, 265); Line(7, 12, 13, 7); break;
            case AppIcon.Trash: Line(3, 5, 17, 5); Line(7, 2, 13, 2); g.DrawLines(pen, [new(5, 5), new(6, 17), new(14, 17), new(15, 5)]); Line(8, 8, 8, 14); Line(12, 8, 12, 14); break;
            case AppIcon.Archive: g.DrawRectangle(pen, 3, 3, 14, 14); Line(3, 7, 17, 7); Line(9, 3, 9, 7); Line(11, 8, 11, 10); Line(9, 10, 9, 12); g.DrawRectangle(pen, 9, 13, 3, 2); break;
            case AppIcon.Image: g.DrawRectangle(pen, 2, 3, 16, 14); g.DrawEllipse(pen, 5, 6, 3, 3); g.DrawLines(pen, [new(3, 16), new(9, 10), new(12, 13), new(15, 10), new(18, 14)]); break;
            case AppIcon.Video: g.DrawRectangle(pen, 2, 4, 16, 12); g.DrawPolygon(pen, [new(8, 7), new(13, 10), new(8, 13)]); break;
            default: g.DrawPolygon(pen, [new(4, 2), new(11, 2), new(16, 7), new(16, 18), new(4, 18)]); g.DrawLines(pen, [new(11, 2), new(11, 7), new(16, 7)]); Line(7, 11, 13, 11); Line(7, 14, 13, 14); break;
        }
        g.Restore(state);
    }
    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

public class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppIcon Glyph { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    bool hot, pressed;
    public ModernButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; AutoSize = false; Height = 38; Cursor = Cursors.Hand; Margin = new(0, 0, 8, 0); }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.DimGray); var scale = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? p.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = Primary && Enabled ? p.Accent : hot && Enabled ? p.Soft : Quiet ? Parent?.BackColor ?? p.Surface : p.Surface;
        if (pressed && Enabled) fill = AppPalette.Blend(fill, p.Text, .1);
        var text = !Enabled ? AppPalette.Blend(p.Muted, p.Surface, .45) : Primary ? p.AccentText : p.Text;
        using var shape = IconDrawing.Rounded(new(1, 1, Width - 3, Height - 3), (int)(7 * scale));
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, shape);
        using (var pen = new Pen(Focused ? p.AccentInk : Primary && Enabled ? fill : p.Border, Focused ? 2 : 1)) e.Graphics.DrawPath(pen, shape);
        int icon = Glyph == AppIcon.None ? 0 : (int)(17 * scale), gap = icon > 0 && Text.Length > 0 ? (int)(7 * scale) : 0;
        var textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int total = Math.Min(Width - (int)(18 * scale), textSize.Width + icon + gap), x = Math.Max((int)(8 * scale), (Width - total) / 2);
        if (icon > 0) IconDrawing.Draw(e.Graphics, Glyph, new(x, (Height - icon) / 2, icon, icon), text);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + icon + gap, 0, Math.Max(1, Width - x - icon - gap - (int)(8 * scale)), Height), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

public class IconView : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppIcon Glyph { get; set; } = AppIcon.File;
    public IconView() { DoubleBuffered = true; TabStop = false; Size = new(24, 24); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); int side = Math.Min(Width, Height); IconDrawing.Draw(e.Graphics, Glyph, new((Width - side) / 2, (Height - side) / 2, side, side), ForeColor); }
}

public class ModernCheckBox : CheckBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    public ModernCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.Gray); e.Graphics.Clear(BackColor); float scale = DeviceDpi / 96f;
        int side = (int)(16 * scale); var box = new Rectangle(1, (Height - side) / 2, side, side);
        using var fill = new SolidBrush(Checked ? p.Accent : p.Surface); using var border = new Pen(Checked ? p.Accent : p.Muted);
        using var path = IconDrawing.Rounded(box, (int)(3 * scale)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        if (Checked) { using var tick = new Pen(p.AccentText, 1.8f * scale); e.Graphics.DrawLines(tick, [new PointF(box.X + 3 * scale, box.Y + 8 * scale), new(box.X + 6 * scale, box.Y + 11 * scale), new(box.X + 13 * scale, box.Y + 4 * scale)]); }
        var text = new Rectangle(side + (int)(10 * scale), 0, Math.Max(1, Width - side - (int)(12 * scale)), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? p.Text : p.Muted, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, text, p.Text, BackColor);
    }
}

public class ModernCombo : ComboBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    public ModernCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 32;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.DimGray); bool selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? p.Selection : p.Surface); e.Graphics.FillRectangle(fill, e.Bounds);
        var rect = e.Bounds; rect.Inflate(-8, 0);
        TextRenderer.DrawText(e.Graphics, e.Index >= 0 ? GetItemText(Items[e.Index]) : Text, Font, rect,
            selected ? AppPalette.Readable(p.Text, p.Selection) : p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.DimGray); e.Graphics.Clear(Parent?.BackColor ?? p.Background);
        using var fill = new SolidBrush(p.Surface); using var path = IconDrawing.Rounded(new(0, 0, Width - 1, Height - 1), 6); e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Focused ? p.AccentInk : p.Border); e.Graphics.DrawPath(pen, path);
        int size = (int)(14 * DeviceDpi / 96f); var text = new Rectangle(9, 0, Math.Max(1, Width - size - 25), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? p.Text : p.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        int cx = Width - size / 2 - 10, cy = Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var arrow = new Pen(p.Muted, 1.5f); e.Graphics.DrawLines(arrow, [new Point(cx - 4, cy - 2), new(cx, cy + 2), new(cx + 4, cy - 2)]);
    }
}

public class ModernProgress : Control
{
    int value, phase;
    ProgressBarStyle style;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { this.value = Math.Clamp(value, 0, 100); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProgressBarStyle Style { get => style; set { style = value; UpdateAnimation(); Invalidate(); } }
    public ModernProgress() { DoubleBuffered = true; TabStop = false; Height = 6; AccessibleRole = AccessibleRole.ProgressBar; timer.Tick += (_, _) => { phase = (phase + 3) % 140; Invalidate(); }; }
    void UpdateAnimation() => timer.Enabled = style == ProgressBarStyle.Marquee && Visible && IsHandleCreated;
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateAnimation(); }
    protected override AccessibleObject CreateAccessibilityInstance() => new ProgressAccess(this);
    sealed class ProgressAccess(ModernProgress owner) : ControlAccessibleObject(owner) { public override string? Value => owner.Style == ProgressBarStyle.Marquee ? Locale.T("Выполняется") : owner.Value + "%"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.DimGray); e.Graphics.Clear(Parent?.BackColor ?? p.Surface);
        int height = Math.Max(3, Math.Min(Height, (int)(6 * DeviceDpi / 96f))); var rect = new Rectangle(0, (Height - height) / 2, Width, height);
        using var track = new SolidBrush(p.Soft); using var path = IconDrawing.Rounded(rect, height / 2); e.Graphics.FillPath(track, path);
        var mark = style == ProgressBarStyle.Marquee ? new Rectangle((int)((phase - 35) / 100.0 * Width), rect.Y, Math.Max(1, Width / 4), height) : new Rectangle(0, rect.Y, (int)(Width * value / 100.0), height);
        mark.Intersect(rect); if (mark.Width > 0) { using var fill = new SolidBrush(AppPalette.Readable(p.Accent, p.Soft, 3)); using var rounded = IconDrawing.Rounded(mark, height / 2); e.Graphics.FillPath(fill, rounded); }
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}

public class SurfacePanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Outline { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor { get; set; }
    public SurfacePanel() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if (Outline) { using var pen = new Pen(LineColor); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); } }
}

public class WorkspaceGrid : DataGridView
{
    public WorkspaceGrid() { DoubleBuffered = true; }
    public Action<int>? WheelScroll;
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (WheelScroll is null) { base.OnMouseWheel(e); return; }
        int lines = SystemInformation.MouseWheelScrollLines;
        WheelScroll(-Math.Sign(e.Delta) * (lines < 0 ? Math.Max(1, DisplayedRowCount(false)) : Math.Max(1, lines)));
    }
}

public class GridHost : SurfacePanel
{
    readonly DataGridView grid;
    readonly ThemeScrollBar scrollbar = new() { Dock = DockStyle.Fill, AccessibleName = "Прокрутка списка" };
    bool syncing;
    public GridHost(DataGridView grid)
    {
        this.grid = grid; Margin = Padding.Empty;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.ColumnStyles.Add(new(SizeType.Absolute, 14)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        grid.ScrollBars = ScrollBars.Horizontal; grid.Margin = Padding.Empty;
        layout.Controls.Add(grid, 0, 0); layout.Controls.Add(scrollbar, 1, 0); Controls.Add(layout);
        scrollbar.ValueChanged += (_, _) => SetFirst(scrollbar.Value);
        if (grid is WorkspaceGrid themed) themed.WheelScroll = delta => scrollbar.Value += delta;
        grid.Scroll += (_, _) => UpdateRange(); grid.RowsAdded += (_, _) => UpdateRange(); grid.RowsRemoved += (_, _) => UpdateRange();
        grid.SizeChanged += (_, _) => UpdateRange(); grid.HandleCreated += (_, _) => UpdateRange(); grid.ColumnHeadersHeightChanged += (_, _) => UpdateRange();
        Layout += (_, _) => UpdateRange();
    }
    void SetFirst(int value)
    {
        if (syncing || grid.RowCount == 0 || !grid.IsHandleCreated) return;
        try { grid.FirstDisplayedScrollingRowIndex = Math.Clamp(value, 0, grid.RowCount - 1); }
        catch (InvalidOperationException) { }
        catch (ArgumentOutOfRangeException) { }
        UpdateRange();
    }
    void UpdateRange()
    {
        if (syncing || grid.IsDisposed) return;
        syncing = true;
        try
        {
            int visible = grid.IsHandleCreated ? grid.DisplayedRowCount(false) : 0;
            scrollbar.Viewport = Math.Max(1, visible); scrollbar.Maximum = Math.Max(0, grid.RowCount - Math.Max(1, visible));
            scrollbar.Value = Math.Max(0, grid.FirstDisplayedScrollingRowIndex);
            scrollbar.Visible = scrollbar.Maximum > 0; scrollbar.Invalidate();
        }
        finally { syncing = false; }
    }
}

public class ThemeScrollBar : Control
{
    int value, maximum, viewport = 1, dragY, dragValue;
    bool dragging, hot;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => maximum; set { maximum = Math.Max(0, value); Value = this.value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Viewport { get => viewport; set { viewport = Math.Max(1, value); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { int next = Math.Clamp(value, 0, maximum); if (next == this.value) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ValueChanged;
    public ThemeScrollBar() { DoubleBuffered = true; Width = 14; Margin = Padding.Empty; TabStop = true; AccessibleRole = AccessibleRole.ScrollBar; SetStyle(ControlStyles.Selectable, true); }
    Rectangle Thumb
    {
        get { int height = Math.Min(Height, Math.Max((int)(26 * DeviceDpi / 96f), (int)(Height * (double)viewport / Math.Max(1, maximum + viewport)))); return new(Math.Max(2, Width / 4), maximum > 0 ? (int)((Height - height) * (double)value / maximum) : 0, Math.Max(3, Width / 2), height); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.Gray); e.Graphics.Clear(BackColor); var thumb = Thumb;
        if (thumb.Width <= 0 || thumb.Height <= 0) return;
        using var brush = new SolidBrush(Focused || hot || dragging ? p.Muted : AppPalette.Blend(p.Muted, BackColor, .35));
        using var path = IconDrawing.Rounded(thumb, thumb.Width / 2); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(brush, path);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (Thumb.Contains(e.Location)) { dragging = true; dragY = e.Y; dragValue = value; Capture = true; }
        else Value += e.Y < Thumb.Top ? -viewport : viewport;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Value = dragValue + (int)Math.Round((e.Y - dragY) * (double)maximum / Math.Max(1, Height - Thumb.Height)); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value -= Math.Sign(e.Delta) * Math.Max(1, SystemInformation.MouseWheelScrollLines); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode) { case Keys.Up: Value--; break; case Keys.Down: Value++; break; case Keys.PageUp: Value -= viewport; break; case Keys.PageDown: Value += viewport; break; case Keys.Home: Value = 0; break; case Keys.End: Value = maximum; break; default: return; } e.Handled = true;
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ScrollAccess(this);
    sealed class ScrollAccess(ThemeScrollBar owner) : ControlAccessibleObject(owner) { public override string? Value { get => owner.Value.ToString(); set { if (int.TryParse(value, out int n)) owner.Value = n; } } }
}
