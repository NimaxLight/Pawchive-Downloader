namespace Pawchive;

// All filtering in this app rebuilds the view: rows are never hidden in place.
// Avoid WinForms GetVisibleIndex, which repeatedly walks the entire grid when
// accessibility clients request a row or cell name (dotnet/winforms#15171).
// Deriving from the standard providers preserves navigation, values and actions.
public class FastTextColumn : DataGridViewTextBoxColumn
{
    public FastTextColumn() => CellTemplate = new FastTextCell();
}
public class FastTextCell : DataGridViewTextBoxCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => new CellAccess(this);
    sealed class CellAccess(FastTextCell cell) : DataGridViewTextBoxCellAccessibleObject(cell)
    {
        public override string Name => $"{cell.OwningColumn?.HeaderText} {Locale.T("Строка")} {cell.RowIndex + 1}";
    }
}
public class FastRow : DataGridViewRow
{
    protected override AccessibleObject CreateAccessibilityInstance() => new RowAccess(this);
    sealed class RowAccess(FastRow row) : DataGridViewRowAccessibleObject(row)
    {
        public override string Name => $"{Locale.T("Строка")} {row.Index + 1}";
        public override AccessibleObject? Navigate(AccessibleNavigation direction)
        {
            if (row.DataGridView is not DataGridView grid) return null;
            if (direction is AccessibleNavigation.Next or AccessibleNavigation.Down)
                return row.Index >= 0 && row.Index + 1 < grid.Rows.Count ? grid.Rows[row.Index + 1].AccessibilityObject : null;
            if (direction is AccessibleNavigation.Previous or AccessibleNavigation.Up)
                return row.Index > 0 ? grid.Rows[row.Index - 1].AccessibilityObject : grid.ColumnHeadersVisible ? grid.AccessibilityObject.GetChild(0) : null;
            return base.Navigate(direction);
        }
    }
}
