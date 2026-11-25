using System.Security.Cryptography;
using System.Text;
using FiveHead.Mcp.Core.Memory;

namespace FiveHead.Mcp.Core.Storage;

/// <summary>
/// Cross-process exclusive locks, one per memory file, acquired in a fixed order.
/// The lock is an exclusive <see cref="FileStream"/> (<c>FileShare.None</c>, which
/// .NET implements with <c>flock</c> on Linux). It is not tied to the thread that
/// acquired it, and the kernel drops it if the process exits.
/// </summary>
internal static class MemoryWriteLock
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<IAsyncDisposable> AcquireAsync(string memoryRoot, CancellationToken ct)
    {
        var root = Path.GetFullPath(memoryRoot);
        var handles = new List<FileStream>();
        try
        {
            foreach (var file in Enum.GetValues<MemoryFile>())
            {
                var path = Path.Combine(root, MemoryLimits.FileName(file));
                handles.Add(await AcquireOneAsync(path, ct));
            }

            return new Composite(handles);
        }
        catch
        {
            foreach (var handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    private static async Task<FileStream> AcquireOneAsync(string memoryFilePath, CancellationToken ct)
    {
        var lockPath = LockPathFor(memoryFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var started = Environment.TickCount64;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Environment.TickCount64 - started < Timeout.TotalMilliseconds)
            {
                await Task.Delay(25, ct);
            }
            catch (IOException)
            {
                throw new MemoryLockTimeoutException(memoryFilePath);
            }
        }
    }

    private static string LockPathFor(string memoryFilePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(memoryFilePath))).ToLowerInvariant();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : home;
        return Path.Combine(root, ".agent-memory", "locks", hash + ".lock");
    }

    private sealed class Composite(List<FileStream> handles) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            foreach (var handle in Enumerable.Reverse(handles))
            {
                handle.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class MemoryLockTimeoutException(string path)
    : TimeoutException($"Timed out waiting for the memory write lock on '{path}'. Another writer still holds it.")
{
    public string Path { get; } = path;
}
