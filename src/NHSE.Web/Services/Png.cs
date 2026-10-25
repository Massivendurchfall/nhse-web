using System.Buffers.Binary;
using System.IO.Compression;

namespace NHSE.Web.Services;

/// <summary>
/// A small PNG encoder for RGBA pixels, enough for design previews and downloads
/// (System.Drawing does not exist on WebAssembly).
/// </summary>
public static class Png
{
    /// <summary>Encodes RGBA pixels, every pixel blown up to a <paramref name="scale"/> x <paramref name="scale"/> square.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, int scale = 1)
    {
        int w = width * scale, h = height * scale;
        var raw = new byte[h * (1 + (w * 4))];
        for (int y = 0; y < h; y++)
        {
            var row = y * (1 + (w * 4)); // filter byte 0 (none) in front of every row
            for (int x = 0; x < w; x++)
                rgba.Slice((((y / scale) * width) + (x / scale)) * 4, 4).CopyTo(raw.AsSpan(row + 1 + (x * 4)));
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            z.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, w);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), h);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    public static string DataUrl(ReadOnlySpan<byte> rgba, int width, int height, int scale = 1) =>
        "data:image/png;base64," + Convert.ToBase64String(Encode(rgba, width, height, scale));

    private static void Chunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
            body[i] = (byte)type[i];
        data.CopyTo(body.AsSpan(4));
        png.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(body));
        png.Write(number);
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
