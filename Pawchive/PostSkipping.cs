namespace Pawchive;

public partial class MainForm
{
    readonly ModernButton skipPost = new() { Text = "Пропустить пост", Enabled = false };
    CancellationTokenSource? currentPostCancellation;
    bool postSkipRequested;
    CancellationTokenSource BeginCurrentPost(CancellationToken queueToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(queueToken);
        currentPostCancellation = cancellation; postSkipRequested = false;
        skipPost.Enabled = !queueToken.IsCancellationRequested;
        return cancellation;
    }
    void EndCurrentPost(CancellationTokenSource cancellation)
    {
        if (currentPostCancellation == cancellation) currentPostCancellation = null;
        skipPost.Enabled = false;
    }
    void SkipCurrentPost()
    {
        if (currentPostCancellation is null || currentPostCancellation.IsCancellationRequested || operation?.IsCancellationRequested == true) return;
        postSkipRequested = true; skipPost.Enabled = false;
        currentPostCancellation.Cancel();
        status.Text = "Пропускаю текущий пост…";
    }
    async Task RecordSkippedPost(Downloader downloader, Post post)
    {
        await Task.Run(() => downloader.MarkSkipped(post));
        post.Error = ""; post.Warning = History.SkippedMessage;
        post.State = "Пропущен / повторить";
        ResetTransfers("Пост пропущен. Следующий запуск скачает недостающее.");
        RefreshRow(post);
    }
}
