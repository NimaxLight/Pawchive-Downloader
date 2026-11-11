namespace Pawchive;

public partial class MainForm
{
    void ConfigurePostColumns()
    {
        if (!table.Columns.Contains("State")) return;
        float scale = DeviceDpi / 96f;
        int WidthFor(int minimum, params string[] labels) => Math.Max((int)(minimum * scale), labels.Max(t => TextRenderer.MeasureText(t, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width) + (int)(24 * scale));
        table.AllowUserToResizeColumns = false; table.ShowCellToolTips = true;
        foreach (DataGridViewColumn column in table.Columns) column.Resizable = DataGridViewTriState.False;
        table.Columns["Date"]!.Width = WidthFor(96, "Дата", "Date", "2026-12-31");
        table.Columns["Files"]!.Width = WidthFor(60, "Файлы", "Files", "999999");
        table.Columns["Links"]!.Width = WidthFor(60, "Ссылки", "Links", "999999");
        table.Columns["Skipped"]!.Width = WidthFor(76, "Пропуски", "Skipped", "999999");
        table.Columns["State"]!.Width = WidthFor(150, "Статус", "Status", "Не скачан", "Неполный пост", "Частично скачан", "Нужна распаковка", "Ошибка / повторить", "Не хватает файлов", "Пост обновлён", "Скачивается…", "Пропущен / повторить", "Not downloaded", "Incomplete post", "Partially downloaded", "Needs extraction", "Error / retry", "Missing files", "Post updated", "Downloading…", "Skipped / retry", "Files: 999/999");
        table.Columns["Author"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        table.Columns["Author"]!.MinimumWidth = (int)(150 * scale); table.Columns["Author"]!.FillWeight = 25;
        table.Columns["Title"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        table.Columns["Title"]!.MinimumWidth = (int)(240 * scale); table.Columns["Title"]!.FillWeight = 75;
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); ConfigurePostColumns(); }
}
