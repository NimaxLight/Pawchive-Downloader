using System.ComponentModel;
namespace Pawchive;

public sealed class EmbeddedDialog : Panel
{
    DialogResult result;
    public Size RequestedSize { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Button? AcceptButton { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Button? CancelButton { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Control? InitialFocus { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Func<Keys, bool>? KeyHandler { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get => result; set { result = value; if (value != DialogResult.None) ResultChosen?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ResultChosen;
    public EmbeddedDialog(string title, Size size) { Text = title; RequestedSize = size; Dock = DockStyle.Fill; Margin = Padding.Empty; DoubleBuffered = true; }
}

public partial class MainForm
{
    sealed record EmbeddedSession(EmbeddedDialog Dialog, CreatorBackdrop Backdrop, CreatorManagerCard Card, Control? PreviousFocus, TaskCompletionSource<DialogResult> Completion);
    readonly List<EmbeddedSession> embeddedSessions = [];
    EmbeddedDialog? embeddedDialog;
    EmbeddedDialog Dialog(string title, Size size) => new(Locale.T(title), size) { Font = Font };
    Task<DialogResult> ShowEmbeddedDialog(EmbeddedDialog dialog, params Control[] opaqueText)
    {
        datePopup?.Close();
        var snapshot = new Bitmap(ClientSize.Width, ClientSize.Height);
        Controls.Cast<Control>().First(c => c.Dock == DockStyle.Fill && c.Visible).DrawToBitmap(snapshot, new Rectangle(Point.Empty, ClientSize));
        var backdrop = new CreatorBackdrop(snapshot) { Dock = DockStyle.Fill };
        var card = new CreatorManagerCard { Dock = DockStyle.None, Text = Locale.T(dialog.Text), LineColor = palette.Border };
        var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new EmbeddedSession(dialog, backdrop, card, FocusedCreatorControl(this), completion);
        var layout = Compose(1, 2); layout.Padding = new Padding(1); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = Compose(2, 1); heading.Padding = new Padding(24, 12, 16, 8); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        heading.Controls.Add(new Label { Text = dialog.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 15), AutoEllipsis = true, Margin = Padding.Empty }, 0, 0);
        var close = new ModernButton { Text = "×", AccessibleName = "Закрыть", Dock = DockStyle.Fill, Margin = Padding.Empty };
        close.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel; heading.Controls.Add(close, 1, 0);
        layout.Controls.Add(WithRules(heading, RuleEdges.Bottom), 0, 0); layout.Controls.Add(dialog, 0, 1); card.Controls.Add(layout);
        void Finish(object? sender, EventArgs e) => CloseEmbeddedDialog(session, dialog.DialogResult);
        dialog.ResultChosen += Finish;
        foreach (var button in Descendants(dialog).OfType<Button>().Where(b => b.DialogResult != DialogResult.None))
        {
            var chosenResult = button.DialogResult; button.DialogResult = DialogResult.None;
            button.Click += (_, _) => dialog.DialogResult = chosenResult;
        }
        backdrop.MouseDown += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        void Arrange()
        {
            float scale = DeviceDpi / 96f;
            card.Size = new Size(Math.Min((int)(dialog.RequestedSize.Width * scale), Math.Max(1, backdrop.Width - 32)), Math.Min((int)((dialog.RequestedSize.Height + 60) * scale), Math.Max(1, backdrop.Height - 32)));
            card.Location = new Point((backdrop.Width - card.Width) / 2, (backdrop.Height - card.Height) / 2);
        }
        embeddedSessions.Add(session); embeddedDialog = dialog; Surface(card);
        backdrop.Controls.Add(card); Controls.Add(backdrop); backdrop.BringToFront(); backdrop.Resize += (_, _) => Arrange(); Arrange();
        ThemeTree(card); UiLanguage.Bind(card, opaqueText); KeyPreview = true;
        if (dialog.InitialFocus?.CanFocus == true) dialog.InitialFocus.Focus(); else dialog.SelectNextControl(null, true, true, true, false);
        return completion.Task;
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    void CloseEmbeddedDialog(EmbeddedSession session, DialogResult result)
    {
        int index = embeddedSessions.IndexOf(session); if (index < 0) return;
        while (embeddedSessions.Count > index + 1) CloseEmbeddedDialog(embeddedSessions[^1], DialogResult.Cancel);
        // The caller reads its controls after awaiting the result and owns disposal.
        session.Dialog.Parent?.Controls.Remove(session.Dialog);
        surfaceControls.Remove(session.Card); embeddedSessions.RemoveAt(index); session.Backdrop.Dispose();
        embeddedDialog = embeddedSessions.LastOrDefault()?.Dialog;
        if (session.PreviousFocus is { IsDisposed: false, CanFocus: true } previous) previous.Focus();
        session.Completion.TrySetResult(result);
    }
    void CloseAllEmbeddedDialogs()
    {
        while (embeddedSessions.Count > 0) CloseEmbeddedDialog(embeddedSessions[^1], DialogResult.Cancel);
    }
    bool EmbeddedDialogKey(Keys keyData)
    {
        if (embeddedDialog is null) return false;
        if (embeddedDialog.KeyHandler?.Invoke(keyData) == true) return true;
        if (keyData is Keys.Tab or (Keys.Tab | Keys.Shift))
        { embeddedDialog.Parent!.Parent!.SelectNextControl(FocusedCreatorControl(embeddedDialog.Parent.Parent), keyData == Keys.Tab, true, true, true); return true; }
        if (keyData == Keys.Escape)
        {
            if (FocusedCreatorControl(embeddedDialog) is ModernCombo { DroppedDown: true } combo) combo.DroppedDown = false;
            else embeddedDialog.DialogResult = DialogResult.Cancel;
            return true;
        }
        if (keyData == Keys.Enter && FocusedCreatorControl(embeddedDialog) is not ModernCombo { DroppedDown: true })
        { if (FocusedCreatorControl(embeddedDialog.Parent!.Parent!) is Button { Enabled: true } focused) focused.PerformClick(); else if (embeddedDialog.AcceptButton is { Enabled: true } accept) accept.PerformClick(); return true; }
        return false;
    }
    async Task ShowMessage(string title, string message)
    {
        if (IsDisposed || Disposing) return;
        using var dialog = Dialog(title, new Size(620, 260));
        var layout = Compose(1, 2); layout.Padding = new Padding(24, 18, 24, 20); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var text = new TextBox { Text = Locale.Error(message), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false };
        var close = new ModernButton { Text = "Готово", Primary = true, Width = 130, Dock = DockStyle.Right, DialogResult = DialogResult.OK };
        layout.Controls.Add(text, 0, 0); layout.Controls.Add(close, 0, 1); dialog.Controls.Add(layout); dialog.AcceptButton = close; dialog.CancelButton = close; dialog.InitialFocus = close;
        await ShowEmbeddedDialog(dialog, text);
    }
    public void ReportError(string message) { UiLanguage.SetError(status, message); _ = ShowMessage("Ошибка", message); }
}
