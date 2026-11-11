using System.Diagnostics;
using Pawchive;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var grid = new DataGridView { AllowUserToAddRows = false, RowTemplate = new FastRow(), RowHeadersVisible = false };
        for (int i = 0; i < 6; i++) grid.Columns.Add(new FastTextColumn { HeaderText = "Column " + i });
        var rows = Enumerable.Range(0, 10000).Select(i => { var r = new FastRow(); r.CreateCells(grid, i, "title", "date", "files", "links", "state"); return r; }).ToArray();
        grid.Rows.AddRange(rows);
        foreach (string language in new[] { "ru", "en" })
        {
        Locale.Language = language; string rowName = language == "en" ? "Row" : "Строка";
        var timer = Stopwatch.StartNew(); int queries = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            var next = row.AccessibilityObject.Navigate(AccessibleNavigation.Next);
            if (row.Index + 1 < grid.Rows.Count && !ReferenceEquals(next, grid.Rows[row.Index + 1].AccessibilityObject)) throw new Exception("Incorrect next row");
            var previous = row.AccessibilityObject.Navigate(AccessibleNavigation.Previous);
            if (row.Index > 0 && !ReferenceEquals(previous, grid.Rows[row.Index - 1].AccessibilityObject)) throw new Exception("Incorrect previous row");
            if (row.AccessibilityObject.Name != rowName + " " + (row.Index + 1)) throw new Exception("Incorrect row name"); queries++;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell.AccessibilityObject.Name != cell.OwningColumn!.HeaderText + " " + rowName + " " + (cell.RowIndex + 1)) throw new Exception("Incorrect cell name");
                if (cell.AccessibilityObject.Value != cell.FormattedValue?.ToString()) throw new Exception("Cell value unavailable"); queries++;
            }
        }
        timer.Stop();
        Console.WriteLine($"PASS ({language}): {queries} accessibility name queries plus 20000 sibling navigations on 10000 rows in {timer.Elapsed.TotalMilliseconds:0} ms; standard cell values retained.");
        if (timer.Elapsed.TotalSeconds > 10) throw new Exception("Accessibility lookup performance regressed");
        }
        Locale.Language = "ru";
        grid.Sort(grid.Columns[0], System.ComponentModel.ListSortDirection.Descending);
        if (grid.Rows[0].AccessibilityObject.Name != "Строка 1") throw new Exception("Row numbers not updated after sorting");
        Console.WriteLine("PASS: accessibility row numbering follows sorting.");
    }
}
