using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Pawchive;

public record ExtractedFile(string Path, long Length);

public static class Archives
{
    static readonly string[] extensions = [".zip", ".rar", ".7z", ".tar", ".gz", ".gzip", ".bz2", ".xz", ".tgz", ".tbz2", ".txz"];
    public static bool IsArchive(string name) => extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    public static bool Present(string root, FileEntry file)
    {
        if (!file.ExtractionComplete || file.ExtractedFolder.Length == 0) return false;
        try
        {
            if (!Directory.Exists(Storage.Inside(root, file.ExtractedFolder))) return false;
            return file.ExtractedFiles.All(f => { var info = new FileInfo(Storage.Inside(root, f.Path)); return info.Exists && info.Length == f.Length; });
        }
        catch { return false; }
    }
    public static Task Extract(string root, FileEntry file, string originalName, CancellationToken token) => Task.Run(() =>
    {
        var source = Storage.Inside(root, file.RelativePath);
        var parent = Path.GetDirectoryName(source)!;
        var staging = Storage.Inside(parent, ".extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var extracted = new List<(string Relative, long Length)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool promoted = false;
        try
        {
            void Copy(IEntry entry, Func<Stream> open)
            {
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new IOException("Ссылки на файлы внутри архива не поддерживаются.");
                if (entry.IsEncrypted) throw new IOException("Архив защищён паролем. Оригинал сохранён; распакуйте его вручную.");
                var name = entry.Key?.Replace('\\', '/') ?? "";
                if (string.IsNullOrWhiteSpace(name)) name = Storage.SafeName(Path.GetFileNameWithoutExtension(originalName));
                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p == "..")) throw new IOException("Недопустимый путь в архиве.");
                var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").Select(p => Storage.SafeName(p, 120)).ToArray();
                if (parts.Length == 0) { if (entry.IsDirectory) return; throw new IOException("Пустое имя файла в архиве."); }
                var relative = Path.Combine(parts);
                var output = Storage.Inside(staging, relative);
                if (entry.IsDirectory) { Directory.CreateDirectory(output); return; }
                if (!seen.Add(relative)) throw new IOException("Архив содержит совпадающие имена файлов.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var input = open();
                using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[131072];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var count = input.Read(buffer);
                    if (count == 0) break;
                    target.Write(buffer, 0, count);
                }
                if (entry.Size > 0 && target.Length != entry.Size) throw new IOException("Архив распакован не полностью.");
                extracted.Add((relative, target.Length));
            }
            var extension = Path.GetExtension(originalName).ToLowerInvariant();
            if (extension is ".zip" or ".rar" or ".7z" or ".tar")
            {
                using var archive = ArchiveFactory.OpenArchive(source);
                if (!archive.IsComplete) throw new IOException("Архив неполный или не хватает его частей.");
                // Sequential reader avoids repeated decompression of solid RAR/7z archives.
                if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                {
                    using var reader = archive.ExtractAllEntries();
                    while (reader.MoveToNextEntry()) Copy(reader.Entry, reader.OpenEntryStream);
                }
                else foreach (var entry in archive.Entries) Copy(entry, entry.OpenEntryStream);
            }
            else
            {
                using var stream = File.OpenRead(source);
                using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions { ExtensionHint = originalName });
                while (reader.MoveToNextEntry()) Copy(reader.Entry, reader.OpenEntryStream);
            }
            token.ThrowIfCancellationRequested();
            var baseName = Storage.SafeName(Path.GetFileNameWithoutExtension(file.RelativePath), 70) + "_unpacked";
            var destination = Storage.Inside(parent, baseName);
            for (int suffix = 2; Directory.Exists(destination) || File.Exists(destination); suffix++) destination = Storage.Inside(parent, baseName + "_" + suffix);
            Directory.Move(staging, destination); promoted = true;
            file.ExtractedFolder = Path.GetRelativePath(root, destination);
            file.ExtractedFiles = extracted.Select(e => new ExtractedFile(Path.GetRelativePath(root, Path.Combine(destination, e.Relative)), e.Length)).ToList();
            file.ExtractionComplete = true; file.ExtractionError = "";
        }
        finally
        {
            // Only the fresh directory owned by this attempt is removed, never existing output.
            if (!promoted && Directory.Exists(staging)) Directory.Delete(Storage.Inside(parent, Path.GetFileName(staging)), true);
        }
    }, token);
}
