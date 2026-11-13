using System.Globalization;
namespace Pawchive;
public sealed class DateCalendar : UserControl
{
    readonly TableLayoutPanel content = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10), Margin = Padding.Empty };
    readonly TableLayoutPanel cells = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly FlowLayoutPanel header = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
    readonly Action<DateOnly?> choose; readonly Action<Control> theme; readonly DateOnly? selected;
    int year, month, level;
    public DateCalendar(DateOnly? selected, Action<DateOnly?> choose, Action<Control> theme)
    {
        this.selected = selected; this.choose = choose; this.theme = theme;
        var initial = selected ?? DateOnly.FromDateTime(DateTime.Today); year = initial.Year; month = initial.Month;
        Size = new Size(350, 360); Font = new Font("Segoe UI", 10); Controls.Add(content);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.Controls.Add(header, 0, 0); content.Controls.Add(cells, 0, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var today = Button(Locale.T("Сегодня"), 150, () => choose(DateOnly.FromDateTime(DateTime.Today)));
        var clear = Button(Locale.T("Очистить дату"), 150, () => choose(null)); today.Dock = clear.Dock = DockStyle.Fill;
        footer.Controls.Add(today, 0, 0); footer.Controls.Add(clear, 1, 0); content.Controls.Add(footer, 0, 2); Render();
    }
    ModernButton Button(string text, int width, Action click)
    { var b = new ModernButton { Text = text, Width = width, Height = 34, Margin = new Padding(2), AccessibleName = text }; b.Click += (_, _) => click(); return b; }
    void Render()
    {
        foreach (Control c in header.Controls.Cast<Control>().ToArray()) c.Dispose();
        foreach (Control c in cells.Controls.Cast<Control>().ToArray()) c.Dispose();
        header.Controls.Clear(); cells.Controls.Clear(); cells.ColumnStyles.Clear(); cells.RowStyles.Clear();
        header.Controls.Add(Button("‹", 30, () => Step(-1)));
        var culture = CultureInfo.GetCultureInfo(Locale.Language == "en" ? "en-US" : "ru-RU");
        if (level == 0) header.Controls.Add(Button(culture.DateTimeFormat.GetMonthName(month), 150, () => { level = 1; Render(); }));
        header.Controls.Add(Button(level == 2 ? $"{year / 12 * 12}–{year / 12 * 12 + 11}" : year.ToString(), level == 0 ? 80 : 234, () => { level = 2; Render(); }));
        header.Controls.Add(Button("›", 30, () => Step(1)));
        cells.ColumnCount = level == 0 ? 7 : 3; cells.RowCount = level == 0 ? 7 : 4;
        for (int i = 0; i < cells.ColumnCount; i++) cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cells.ColumnCount));
        for (int i = 0; i < cells.RowCount; i++) cells.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / cells.RowCount));
        if (level == 0)
        {
            for (int i = 0; i < 7; i++) cells.Controls.Add(new Label { Text = culture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, i, 0);
            int start = ((int)new DateOnly(year, month, 1).DayOfWeek + 6) % 7;
            for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
            {
                var date = new DateOnly(year, month, day); var b = Button(day.ToString(), 34, () => choose(date)); b.Dock = DockStyle.Fill; b.Primary = selected == date; b.AccessibleName = date.ToString("D", culture);
                int index = start + day - 1; cells.Controls.Add(b, index % 7, 1 + index / 7);
            }
        }
        else for (int i = 0; i < 12; i++)
        {
            int value = level == 1 ? i + 1 : year / 12 * 12 + i;
            var b = Button(level == 1 ? culture.DateTimeFormat.GetAbbreviatedMonthName(value) : value.ToString(), 85, () => { if (level == 1) { month = value; level = 0; } else { year = value; level = 1; } Render(); });
            b.Dock = DockStyle.Fill; b.Enabled = level == 1 || value is >= 1900 and <= 2199; cells.Controls.Add(b, i % 3, i / 3);
        }
        theme(this);
    }
    void Step(int direction)
    {
        if (level == 0) { var date = new DateOnly(year, month, 1).AddMonths(direction); if (date.Year is >= 1900 and <= 2199) { year = date.Year; month = date.Month; } }
        else year = Math.Clamp(year + direction * (level == 2 ? 12 : 1), 1900, 2199);
        Render();
    }
}

public sealed class CalendarPopup : ToolStripDropDown
{
    public CalendarPopup(AppPalette palette)
    {
        AutoSize = false; AutoClose = true; DropShadowEnabled = false;
        Padding = new Padding(1); ApplyPalette(palette);
    }
    public void ApplyPalette(AppPalette palette)
    {
        BackColor = palette.Surface; Renderer = new CalendarRenderer(palette); Invalidate();
    }
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ClassStyle &= ~0x20000; return parameters; }
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); if (Width < 2 || Height < 2) return;
        using var shape = IconDrawing.Rounded(new Rectangle(0, 0, Width, Height), (int)(10 * DeviceDpi / 96f));
        var old = Region; Region = new Region(shape); old?.Dispose();
    }
    sealed class CalendarRenderer(AppPalette palette) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(palette.Surface);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shape = IconDrawing.Rounded(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), (int)(10 * e.ToolStrip.DeviceDpi / 96f));
            using var pen = new Pen(AppPalette.Blend(palette.Border, palette.Muted, .25)); e.Graphics.DrawPath(pen, shape);
        }
    }
}
