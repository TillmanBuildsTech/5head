using System.Text;
using FiveHead.Mcp.Core.Memory;

namespace FiveHead.Mcp.Core.Storage;

public sealed class MemoryFileStore(string memoryRootPath) : IMemoryFileStore
{
    private readonly string _memoryRootPath = Path.GetFullPath(memoryRootPath);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string RootPath => _memoryRootPath;

    public async Task<string> ReadAsync(MemoryFile file, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var path = GetPath(file);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, ct)
            : string.Empty;
    }

    public async Task WriteAsync(MemoryFile file, WriteMode mode, string content, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var current = await ReadAsync(file, ct);
        var next = ApplyMode(current, mode, content);
        var limit = MemoryLimits.For(file);
        if (next.Length > limit)
        {
            throw new SizeLimitExceededException(file, next.Length, limit);
        }

        await ReplaceAllAsync(file, next, ct);
    }

    public async Task ReplaceAllAsync(MemoryFile file, string content, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var path = GetPath(file);
        var directory = Path.GetDirectoryName(path) ?? _memoryRootPath;
        var tmp = Path.Combine(directory, $".{MemoryLimits.FileName(file)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tmp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await using var writer = new StreamWriter(stream, Utf8);
                await writer.WriteAsync((content ?? string.Empty).AsMemory(), ct);
                await writer.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            throw;
        }
    }

    public Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_memoryRootPath);
        foreach (var file in Enum.GetValues<MemoryFile>())
        {
            var path = GetPath(file);
            if (File.Exists(path))
            {
                continue;
            }

            try
            {
                using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        return Task.CompletedTask;
    }

    internal static string ApplyMode(string current, WriteMode mode, string content)
    {
        current ??= string.Empty;
        content ??= string.Empty;

        return mode switch
        {
            WriteMode.Append => Append(current, content),
            WriteMode.Replace => content,
            WriteMode.Remove => current.Replace(content, string.Empty, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private string GetPath(MemoryFile file) => Path.Combine(_memoryRootPath, MemoryLimits.FileName(file));

    private static string Append(string current, string content)
    {
        if (string.IsNullOrEmpty(current))
        {
            return content;
        }

        if (string.IsNullOrEmpty(content))
        {
            return current;
        }

        return current.EndsWith("\n", StringComparison.Ordinal)
            ? current + content
            : current + Environment.NewLine + content;
    }
}
