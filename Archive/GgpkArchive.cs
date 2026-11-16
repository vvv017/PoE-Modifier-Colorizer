using LibBundledGGPK3;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GgpkColorizer.Archive;

internal sealed class GgpkArchive : IDisposable
{
    private static readonly object CodecLock = new();
    private static string? loadedCodecPath;
    private readonly FileStream stream;
    private readonly BundledGGPK archive;
    private bool disposed;

    public GgpkArchive(string path, string codecPath, bool writable = false)
    {
        EnsureCodec(codecPath);
        stream = new FileStream(path, FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            writable ? FileShare.None : FileShare.Read);
        try
        {
            archive = new BundledGGPK(stream, leaveOpen: true, parsePathsInIndex: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public byte[]? Read(string path)
    {
        if (!archive.Index.TryGetFile(path, out var record)) return null;
        if (record.Size > 64 * 1024 * 1024)
            throw new InvalidDataException($"Resource is too large for this editor: {path}");
        return record.Read().ToArray();
    }

    public void Write(string path, byte[] bytes)
    {
        if (!stream.CanWrite) throw new InvalidOperationException("The archive is read-only.");
        if (!archive.Index.TryGetFile(path, out var record))
            throw new FileNotFoundException($"Resource no longer exists: {path}");
        record.Write(bytes, saveIndex: false);
    }

    public void Save()
    {
        if (!stream.CanWrite) throw new InvalidOperationException("The archive is read-only.");
        archive.Index.Save(LibBundle3.Oodle.Compressor.Invalid);
        archive.Flush();
        stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { archive.Dispose(); }
        finally
        {
            stream.Dispose();
            LibBundle3.Oodle.Release();
        }
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void EnsureCodec(string requestedPath)
    {
        string path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(AppContext.BaseDirectory, "oo2core.dll")
            : Path.GetFullPath(requestedPath);
        lock (CodecLock)
        {
            if (loadedCodecPath is not null)
            {
                if (!string.Equals(path, loadedCodecPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Restart the app after changing the codec library.");
                return;
            }
            if (!File.Exists(path))
                throw new FileNotFoundException("Place oo2core.dll beside GgpkColorizer.exe, then reopen the app.", path);
            nint handle = NativeLibrary.Load(path);
            NativeLibrary.SetDllImportResolver(typeof(LibBundle3.Oodle).Assembly,
                (name, assembly, searchPath) => name == "oo2core" ? handle : 0);
            loadedCodecPath = path;
        }
    }
}
