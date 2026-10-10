using System.IO.Hashing;
using System.Text;
using ClipClop2.History;

namespace ClipClop2.Storage;

/// <summary>
/// Pinned entries on disk: one binary blob (every format, byte for byte), encrypted as a whole with
/// DPAPI (CurrentUser). Unpinned history is never written here.
/// </summary>
internal static class PinStore
{
    private static ReadOnlySpan<byte> Magic => "CCP1"u8;

    public static void Save(string path, IReadOnlyList<ClipEntry> pinned)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(pinned.Count);
            foreach (ClipEntry entry in pinned)
            {
                writer.Write((byte)entry.Kind);
                writer.Write(entry.CopiedAt.ToBinary());
                writer.Write(entry.SourceApp);
                writer.Write(entry.SearchText);
                writer.Write(entry.Formats.Count);
                foreach (ClipFormat format in entry.Formats)
                {
                    byte[] data = format.ReadData();
                    writer.Write(format.Id);
                    writer.Write(format.Name);
                    writer.Write(data.Length);
                    writer.Write(data);
                }
            }
        }

        byte[] encrypted = Dpapi.Protect(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The saved pins, or an empty list when there is no file or it cannot be read.</summary>
    public static List<ClipEntry> Load(string path)
    {
        var entries = new List<ClipEntry>();
        if (!File.Exists(path) || Dpapi.Unprotect(File.ReadAllBytes(path)) is not { } plain)
        {
            return entries;
        }

        try
        {
            using var reader = new BinaryReader(new MemoryStream(plain), Encoding.UTF8);
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual(Magic))
            {
                return entries;
            }
            int count = reader.ReadInt32();
            Span<byte> length = stackalloc byte[4];
            for (int i = 0; i < count; i++)
            {
                var kind = (ClipKind)reader.ReadByte();
                DateTime copiedAt = DateTime.FromBinary(reader.ReadInt64());
                string source = reader.ReadString();
                string search = reader.ReadString();
                int formatCount = reader.ReadInt32();
                var formats = new List<ClipFormat>(formatCount);
                var hasher = new XxHash128();
                for (int f = 0; f < formatCount; f++)
                {
                    uint id = reader.ReadUInt32();
                    string name = reader.ReadString();
                    byte[] data = reader.ReadBytes(reader.ReadInt32());
                    // Same hash as ClipCapture, so re-copying a pinned item finds it.
                    hasher.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(name.AsSpan()));
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
                    hasher.Append(length);
                    hasher.Append(data);
                    formats.Add(new ClipFormat(id, name, data));
                }
                entries.Add(new ClipEntry(kind, formats, hasher.GetCurrentHashAsUInt128(), search, source, copiedAt));
            }
        }
        catch (EndOfStreamException)
        {
            // Truncated: keep what was read.
        }
        return entries;
    }
}
