using System.Security.Cryptography;

namespace ClipClop2.Storage;

/// <summary>
/// Large unpinned payloads, spilled out of RAM into DPAPI-encrypted files under
/// <see cref="AppPaths.CacheDirectory"/>. Unpinned history must not outlive the process, so the
/// directory is wiped on start and on exit.
/// </summary>
internal sealed class PayloadCache
{
    /// <summary>Formats larger than this live in the cache instead of RAM.</summary>
    public const int SpillThresholdBytes = 256 * 1024;

    private readonly string directory;

    public PayloadCache(string directory)
    {
        this.directory = directory;
    }

    public void Wipe()
    {
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                TryDelete(file);
            }
        }
        Directory.CreateDirectory(directory);
    }

    /// <summary>Encrypts and stores a payload; returns its file path.</summary>
    public string Write(ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + ".bin");
        File.WriteAllBytes(path, Dpapi.Protect(data));
        return path;
    }

    /// <summary>Reads and decrypts a payload. Throws if the file is gone or was tampered with.</summary>
    public static byte[] Read(string path)
    {
        return Dpapi.Unprotect(File.ReadAllBytes(path))
            ?? throw new InvalidDataException($"Cached payload could not be decrypted: {path}");
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Wiped on the next start.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
