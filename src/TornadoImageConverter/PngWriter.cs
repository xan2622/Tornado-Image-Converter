using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TornadoImageConverter;

/// <summary>
/// Writes an 8-bit indexed PNG (color type 3) keeping the exact order of the indices,
/// which guarantees a lossless round trip to BT2.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void WriteIndexed(Stream output, int width, int height, byte[] pixels, byte[] paletteRgb, bool transparentIndex0)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        if (paletteRgb.Length != 256 * 3)
            throw new ArgumentException("Palette must contain 256 RGB entries.", nameof(paletteRgb));

        WriteHeader(output, width, height, colorType: 3);  // indexed color
        WriteChunk(output, "PLTE", paletteRgb);

        // tRNS: only entry 0 is transparent, the following ones stay opaque
        if (transparentIndex0)
            WriteChunk(output, "tRNS", [0]);

        WriteChunk(output, "IDAT", Compress(width, height, pixels));
        WriteChunk(output, "IEND", []);
    }

    /// <summary>32-bit RGBA PNG (for decoded images without a usable palette).</summary>
    public static void WriteRgba(Stream output, int width, int height, int[] argb)
    {
        if (argb.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {argb.Length}.", nameof(argb));

        WriteHeader(output, width, height, colorType: 6);  // RGBA

        var raw = new byte[height * (1 + width * 4)];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;  // "None" filter
            for (int x = 0; x < width; x++)
            {
                int p = argb[y * width + x];
                raw[o++] = (byte)(p >> 16);
                raw[o++] = (byte)(p >> 8);
                raw[o++] = (byte)p;
                raw[o++] = (byte)(p >> 24);
            }
        }

        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        WriteChunk(output, "IDAT", buffer.ToArray());
        WriteChunk(output, "IEND", []);
    }

    /// <summary>Signature and IHDR chunk, 8 bits per index or per channel.</summary>
    private static void WriteHeader(Stream output, int width, int height, byte colorType)
    {
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = colorType;
        WriteChunk(output, "IHDR", header);
    }

    private static byte[] Compress(int width, int height, byte[] pixels)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);  // "None" filter
                zlib.Write(pixels, y * width, width);
            }
        }
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> word = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = UpdateCrc(0xFFFFFFFFu, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
