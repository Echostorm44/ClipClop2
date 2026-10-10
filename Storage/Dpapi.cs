using System.Runtime.InteropServices;

namespace ClipClop2.Storage;

/// <summary>
/// Windows DPAPI with CurrentUser scope: data encrypted here can only be decrypted by the same
/// Windows user on the same machine, and there is no key for ClipClop2 to store or leak.
/// </summary>
internal static unsafe partial class Dpapi
{
    // Ties blobs to ClipClop2 so another app running as the same user cannot decrypt them by
    // accident (DPAPI's optional entropy; it is not a secret).
    private static ReadOnlySpan<byte> Entropy => "ClipClop2.v1"u8;

    private const uint CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(ReadOnlySpan<byte> plain)
    {
        fixed (byte* plainPtr = plain)
        fixed (byte* entropyPtr = Entropy)
        {
            var input = new DataBlob { Size = (uint)plain.Length, Data = plainPtr };
            var entropy = new DataBlob { Size = (uint)Entropy.Length, Data = entropyPtr };
            if (!CryptProtectData(&input, null, &entropy, 0, 0, CryptProtectUiForbidden, out DataBlob output))
            {
                throw new InvalidOperationException($"CryptProtectData failed (error {Marshal.GetLastPInvokeError()}).");
            }
            return TakeBlob(output);
        }
    }

    /// <summary>Decrypts a blob made by <see cref="Protect"/>; null when it is corrupt or belongs to another user.</summary>
    public static byte[]? Unprotect(ReadOnlySpan<byte> encrypted)
    {
        fixed (byte* encryptedPtr = encrypted)
        fixed (byte* entropyPtr = Entropy)
        {
            var input = new DataBlob { Size = (uint)encrypted.Length, Data = encryptedPtr };
            var entropy = new DataBlob { Size = (uint)Entropy.Length, Data = entropyPtr };
            if (!CryptUnprotectData(&input, null, &entropy, 0, 0, CryptProtectUiForbidden, out DataBlob output))
            {
                return null;
            }
            return TakeBlob(output);
        }
    }

    // Copies DPAPI's LocalAlloc'd buffer into managed memory, then zeroes and frees it so the
    // plaintext does not linger in the native heap.
    private static byte[] TakeBlob(DataBlob blob)
    {
        try
        {
            return new ReadOnlySpan<byte>(blob.Data, (int)blob.Size).ToArray();
        }
        finally
        {
            new Span<byte>(blob.Data, (int)blob.Size).Clear();
            LocalFree(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint Size;
        public byte* Data;
    }

    [LibraryImport("crypt32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DataBlob* dataIn, char* description, DataBlob* entropy, nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [LibraryImport("crypt32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DataBlob* dataIn, char** description, DataBlob* entropy, nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [LibraryImport("kernel32")]
    private static partial nint LocalFree(void* memory);
}
