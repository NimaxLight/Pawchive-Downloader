namespace Pawchive;
public partial class MainForm
{
    void ShowDownloadChoice()
    {
        if (operation is not null || downloadManager is not null) return;
        datePopup?.Close();
        var ordered = OrderedPosts(posts).ToArray(); var active = Selected().Select(c => c.Key).ToHashSet();
        bool valid = Period(out var from, out var to);
        var plans = Enum.GetValues<DownloadMode>().ToDictionary(m => m, m => valid ? DownloadSelection.Build(ordered, active, m, pickedPosts, from, to, settings.RecheckDownloaded) : []);
        var image = new Bitmap(ClientSize.Width, ClientSize.Height); Controls.Cast<Control>().First(c => c.Dock == DockStyle.Fill).DrawToBitmap(image, new Rectangle(Point.Empty, ClientSize));
        var overlay = new CreatorBackdrop(image) { Dock = DockStyle.Fill }; downloadOverlay = overlay;
        var card = new CreatorManagerCard { Text = Locale.T("Что скачать?"), LineColor = palette.Border }; downloadManager = card; Surface(card);
        var layout = Compose(1, 4); layout.Padding = new Padding(24);
        foreach (int height in new[] { 42, 38, 0, 102 }) layout.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
        var heading = Compose(2, 1); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        heading.Controls.Add(new Label { Text = "Что скачать?", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 16), Margin = Padding.Empty }, 0, 0);
        var close = new ModernButton { Text = "×", AccessibleName = Locale.T("Закрыть"), Dock = DockStyle.Fill, Margin = Padding.Empty }; close.Click += (_, _) => CloseDownloadChoice(); heading.Controls.Add(close, 1, 0); layout.Controls.Add(heading, 0, 0);
        var scope = new Label { Text = Locale.T("Активные авторы:") + " " + string.Join(", ", Selected().Select(c => c.Name)), Dock = DockStyle.Fill, Tag = "muted", AutoEllipsis = true, Margin = Padding.Empty };
        tips.SetToolTip(scope, string.Join(", ", Selected().Select(c => c.Name))); layout.Controls.Add(scope, 0, 1);
        var body = Compose(2, 1); body.Padding = new Padding(0, 4, 0, 16); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); layout.Controls.Add(body, 0, 2);
        var choices = Compose(1, 4); choices.Margin = new Padding(0, 0, 22, 0);
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        choices.Controls.Add(new Label { Text = "Режим загрузки", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
        var options = Compose(1, 4); for (int i = 0; i < 4; i++) options.RowStyles.Add(new RowStyle(SizeType.Percent, 25)); choices.Controls.Add(options, 0, 1);
        var period = new SectionPanel { Dock = DockStyle.Fill, Edges = RuleEdges.Top, Padding = new Padding(0, 11, 0, 4), Margin = new Padding(0, 8, 0, 0) };
        var periodLayout = Compose(2, 2); periodLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); periodLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        periodLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); periodLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        periodLayout.Controls.Add(new Label { Text = "Период публикации", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Margin = Padding.Empty }, 0, 0); periodLayout.SetColumnSpan(periodLayout.GetControlFromPosition(0, 0)!, 2);
        periodLayout.Controls.Add(new Label { Text = PeriodCaption(), Dock = DockStyle.Fill, Tag = "muted", AutoEllipsis = true, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var edit = new ModernButton { Text = "Изменить период", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) }; edit.Click += (_, _) => { CloseDownloadChoice(); dateFrom.Focus(); }; periodLayout.Controls.Add(edit, 1, 1);
        period.Controls.Add(periodLayout); choices.Controls.Add(period, 0, 2);
        choices.Controls.Add(new Label { Text = "Общий с таблицей — задаётся над списком постов.", Dock = DockStyle.Fill, Tag = "muted", Font = new Font(Font.FontFamily, 9), Margin = Padding.Empty }, 0, 3);
        body.Controls.Add(choices, 0, 0);
        var previewFrame = new SectionPanel { Dock = DockStyle.Fill, Edges = RuleEdges.Left, Padding = new Padding(20, 0, 0, 0) };
        var preview = Compose(1, 8); preview.AccessibleName = "Предпросмотр очереди";
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); preview.Controls.Add(new Label { Text = "Попадут в очередь", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); preview.Controls.Add(new Label { Text = "В порядке активных авторов", Dock = DockStyle.Fill, Tag = "muted", Font = new Font(Font.FontFamily, 9), Margin = Padding.Empty }, 0, 1);
        var previewItems = new Label[5]; var metadata = new Label[5];
        for (int i = 0; i < 5; i++)
        {
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 59)); var item = Compose(1, 2);
            item.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); item.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewItems[i] = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty };
            metadata[i] = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted", Font = new Font(Font.FontFamily, 9), Margin = Padding.Empty };
            item.Controls.Add(previewItems[i], 0, 0); item.Controls.Add(metadata[i], 0, 1); preview.Controls.Add(item, 0, i + 2);
        }
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var remaining = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted", Margin = Padding.Empty }; preview.Controls.Add(remaining, 0, 7);
        previewFrame.Controls.Add(preview); body.Controls.Add(previewFrame, 1, 0);
        var footer = new SectionPanel { Dock = DockStyle.Fill, Edges = RuleEdges.Top, Padding = new Padding(0, 13, 0, 0) };
        var footerLayout = Compose(2, 2); footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); footer.Controls.Add(footerLayout); layout.Controls.Add(footer, 0, 3);
        var metrics = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var metricLabels = Enumerable.Range(0, 4).Select(_ => new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 22, 0) }).ToArray();
        metrics.Controls.AddRange(metricLabels); footerLayout.Controls.Add(metrics, 0, 0); footerLayout.SetColumnSpan(metrics, 2);
        footerLayout.Controls.Add(new Label { Text = "Готовые файлы заново не скачиваются.", Dock = DockStyle.Fill, Tag = "muted", Font = new Font(Font.FontFamily, 9), Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 0) }, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        var start = new ModernButton { Text = "Начать загрузку", Primary = true, Width = 194, Height = 42, Margin = new Padding(8, 0, 0, 0) };
        var cancel = new ModernButton { Text = "Отмена", Width = 116, Height = 42, Margin = new Padding(0, 0, 0, 0) }; cancel.Click += (_, _) => CloseDownloadChoice();
        actions.Controls.AddRange([start, cancel]); footerLayout.Controls.Add(actions, 1, 1);
        void Update()
        {
            var plan = plans[chosenMode];
            for (int i = 0; i < previewItems.Length; i++)
            {
                if (i < plan.Count)
                {
                    var post = plan[i]; string date = post.Published.Length >= 10 ? post.Published[..10] : post.Published;
                    previewItems[i].Text = post.Title.Replace("\r", " ").Replace("\n", " ");
                    metadata[i].Text = string.Join(" · ", new[] { post.Creator.Name, date, IsNewPost(post) ? Locale.T("Новое") : "" }.Where(value => value.Length > 0));
                }
                else { previewItems[i].Text = ""; metadata[i].Text = ""; }
                tips.SetToolTip(previewItems[i], i < plan.Count ? plan[i].Title : ""); tips.SetToolTip(metadata[i], metadata[i].Text);
            }
            remaining.Text = plan.Count > 5 ? $"И ещё {plan.Count - 5} постов" : plan.Count == 0 ? "Нет подходящих постов" : "";
            metricLabels[0].Text = Locale.T($"К загрузке: {plan.Count}"); metricLabels[1].Text = Locale.T($"Авторов: {plan.Select(p => p.Creator.Key).Distinct().Count()}");
            metricLabels[2].Text = Locale.T($"Отмечено: {pickedPosts.Count}"); metricLabels[3].Text = Locale.T($"В периоде: {posts.Count(p => active.Contains(p.Creator.Key) && pickedPosts.Contains(p.Key) && DownloadSelection.InPeriod(p, from, to))}");
            start.Enabled = plan.Count > 0 && valid;
        }
        string[] titles = [settings.RecheckDownloaded ? "Все посты (проверить и докачать)" : "Все недокачанные", "Только новые", "Повторить проблемные", "Выбранные посты"];
        string[] descriptions = [settings.RecheckDownloaded ? "Проверить доступность файлов, сохранив уже скачанные." : "Новые, незавершённые и требующие повторной попытки.", "Обнаруженные после первой проверки, ещё не скачанные.", "Ошибки, пропуски, неполные посты и распаковка.", "Посты с галочками в таблице, включая другие страницы."];
        foreach (var mode in Enum.GetValues<DownloadMode>())
        {
            var radio = new DownloadOption { Text = Locale.T(titles[(int)mode]), Description = Locale.T(descriptions[(int)mode]), Count = plans[mode].Count, Checked = mode == chosenMode, Dock = DockStyle.Fill };
            radio.AccessibleDescription = radio.Description; radio.CheckedChanged += (_, _) => { if (radio.Checked) { chosenMode = mode; Update(); } }; options.Controls.Add(radio, 0, (int)mode);
        }
        start.Click += async (_, _) => { downloadPlan = plans[chosenMode].ToArray(); CloseDownloadChoice(); await Run(DownloadPosts); };
        card.Controls.Add(layout); overlay.Controls.Add(card); Controls.Add(overlay); overlay.BringToFront();
        void Arrange() { card.Size = new Size(Math.Min((int)(940 * DeviceDpi / 96f), overlay.Width - 32), Math.Min((int)(720 * DeviceDpi / 96f), overlay.Height - 32)); card.Location = new Point((overlay.Width - card.Width) / 2, (overlay.Height - card.Height) / 2); }
        overlay.Resize += (_, _) => Arrange(); overlay.MouseDown += (_, _) => CloseDownloadChoice(); Arrange(); ThemeTree(card);
        UiLanguage.Bind(card, previewItems.Concat(metadata).Append(scope).ToArray()); Update(); start.Focus();
    }
}
