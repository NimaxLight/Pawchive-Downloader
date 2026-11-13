using System.ComponentModel;
using System.Drawing.Drawing2D;
namespace Pawchive;

[Flags] public enum RuleEdges { None = 0, Top = 1, Right = 2, Bottom = 4, Left = 8 }
public class SectionPanel : SurfacePanel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RuleEdges Edges { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(LineColor);
        if (Edges.HasFlag(RuleEdges.Top)) e.Graphics.DrawLine(pen, 0, 0, Width - 1, 0);
        if (Edges.HasFlag(RuleEdges.Bottom)) e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
        if (Edges.HasFlag(RuleEdges.Left)) e.Graphics.DrawLine(pen, 0, 0, 0, Height - 1);
        if (Edges.HasFlag(RuleEdges.Right)) e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height - 1);
    }
}

public class DateEntry : Panel
{
    readonly TextBox input;
    public ModernButton CalendarButton { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette? Palette { get; set; }
    public DateEntry(TextBox input, string accessibleName)
    {
        this.input = input; DoubleBuffered = true; TabStop = false;
        input.BorderStyle = BorderStyle.None; input.Dock = DockStyle.None;
        CalendarButton = new ModernButton { Glyph = AppIcon.Calendar, Integrated = true, AccessibleName = accessibleName, Margin = Padding.Empty };
        Controls.Add(input); Controls.Add(CalendarButton);
        input.Enter += (_, _) => Invalidate(); input.Leave += (_, _) => Invalidate();
        CalendarButton.Enter += (_, _) => Invalidate(); CalendarButton.Leave += (_, _) => Invalidate();
        input.KeyDown += (_, e) => { if (e.KeyCode == Keys.F4 || e.Alt && e.KeyCode == Keys.Down) { CalendarButton.PerformClick(); e.Handled = e.SuppressKeyPress = true; } };
        Click += (_, _) => input.Focus();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (input is null) return;
        int inset = (int)(11 * DeviceDpi / 96f), button = (int)(36 * DeviceDpi / 96f);
        CalendarButton.SetBounds(Width - button - 3, 3, button, Math.Max(1, Height - 6));
        input.SetBounds(inset, Math.Max(0, (Height - input.PreferredHeight) / 2), Math.Max(1, Width - button - inset - 8), input.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.Gray); e.Graphics.Clear(Parent?.BackColor ?? p.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = IconDrawing.Rounded(new(0, 0, Width - 1, Height - 1), (int)(7 * DeviceDpi / 96f));
        using var fill = new SolidBrush(p.Surface); using var pen = new Pen(ContainsFocus ? p.AccentInk : p.Border);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(pen, shape);
    }
}

public class DownloadOption : RadioButton
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public AppPalette? Palette { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Description { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Count { get; set; }
    public DownloadOption()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AutoSize = false; Cursor = Cursors.Hand; Height = 68; Margin = new Padding(0, 0, 0, 7);
        CheckedChanged += (_, _) => Invalidate(); GotFocus += (_, _) => Invalidate(); LostFocus += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Palette ?? AppPalette.Create(false, Color.Gray); float s = DeviceDpi / 96f;
        e.Graphics.Clear(Parent?.BackColor ?? p.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = IconDrawing.Rounded(new(1, 1, Width - 3, Height - 3), (int)(7 * s));
        using var fill = new SolidBrush(Checked ? p.Selection : p.Surface); using var border = new Pen(Checked || Focused ? p.AccentInk : p.Border);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
        var circle = new Rectangle((int)(12 * s), (int)(15 * s), (int)(13 * s), (int)(13 * s));
        using var radioPen = new Pen(Checked ? p.AccentInk : p.Muted); e.Graphics.DrawEllipse(radioPen, circle);
        if (Checked) { circle.Inflate(-(int)(3 * s), -(int)(3 * s)); using var ink = new SolidBrush(p.AccentInk); e.Graphics.FillEllipse(ink, circle); }
        int left = (int)(35 * s), right = (int)(48 * s);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, (int)(10 * s), Math.Max(1, Width - left - right), (int)(23 * s)), p.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, Count.ToString(), Font, new Rectangle(Width - right, (int)(10 * s), right - (int)(10 * s), (int)(23 * s)), p.Muted, TextFormatFlags.Right | TextFormatFlags.SingleLine);
        using var small = new Font(Font.FontFamily, 9);
        TextRenderer.DrawText(e.Graphics, Description, small, new Rectangle(left, (int)(34 * s), Math.Max(1, Width - left - (int)(10 * s)), Height - (int)(38 * s)), p.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
