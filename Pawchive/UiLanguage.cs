using System.Runtime.CompilerServices;

namespace Pawchive;

// Keep canonical source text so switching languages is reversible without rebuilding controls.
static class UiLanguage
{
    sealed class Binding(Control control, bool translateText)
    {
        public string Source = control.Text;
        public string? Accessible = control.AccessibleName;
        public string[]? Items = control is ComboBox combo && control.Tag as string != "language-options" ? combo.Items.Cast<object>().Select(i => i.ToString() ?? "").ToArray() : null;
        public string[]? Headers = control is DataGridView grid ? grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray() : null;
        public bool Updating;
        public bool ErrorText;
        public void Apply()
        {
            if (control.IsDisposed) return;
            Updating = true;
            try
            {
                if (translateText) control.Text = ErrorText ? Locale.Error(Source) : Locale.T(Source);
                if (Accessible is not null) control.AccessibleName = Locale.T(Accessible);
                if (Items is not null && control is ComboBox combo)
                {
                    int selected = combo.SelectedIndex; combo.BeginUpdate();
                    try { for (int i = 0; i < Items.Length; i++) combo.Items[i] = Locale.T(Items[i]); if (selected >= 0) combo.SelectedIndex = selected; }
                    finally { combo.EndUpdate(); }
                }
                if (Headers is not null && control is DataGridView grid)
                    for (int i = 0; i < Headers.Length; i++) grid.Columns[i].HeaderText = Locale.T(Headers[i]);
                control.Invalidate();
            }
            finally { Updating = false; }
        }
        public void Changed()
        {
            if (Updating) return;
            ErrorText = false; Source = control.Text; Apply();
        }
    }
    static readonly ConditionalWeakTable<Control, Binding> bindings = new();
    public static void Bind(Control root, params Control[] opaqueText)
    {
        void Walk(Control control)
        {
            if (!bindings.TryGetValue(control, out var binding))
            {
                bool translate = control is Label or Button or Form or CheckBox && !opaqueText.Contains(control);
                binding = new Binding(control, translate); bindings.Add(control, binding);
                if (translate) control.TextChanged += (_, _) => binding.Changed();
            }
            binding.Apply();
            foreach (Control child in control.Controls) Walk(child);
        }
        Walk(root);
    }
    public static void Refresh(Control root)
    {
        if (bindings.TryGetValue(root, out var binding)) binding.Apply();
        foreach (Control child in root.Controls) Refresh(child);
    }
    public static void SetError(Control control, string message)
    {
        if (bindings.TryGetValue(control, out var binding)) { binding.Source = message; binding.ErrorText = true; binding.Apply(); }
        else control.Text = Locale.Error(message);
    }
}
