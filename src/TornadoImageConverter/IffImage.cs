using System.Buffers.Binary;
using System.Text;

namespace TornadoImageConverter;

/// <summary>IFF/LBM decoder (Deluxe Paint): chunky PBM and planar ILBM, ByteRun1 or uncompressed.</summary>
internal static class IffImage
{
    public static SourceImage Decode(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 12 || Tag(data, 0) != "FORM")
            throw new InvalidDataException("Not an IFF file.");

        string form = Tag(data, 8);
        if (form is not ("PBM " or "ILBM"))
            throw new InvalidDataException($"Unsupported IFF type '{form.Trim()}'.");

        int width = 0, height = 0, planes = 0, masking = 0, compression = 0;
        byte[]? cmap = null;
        int bodyStart = -1, bodyLength = 0;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = Tag(data, pos);
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4)), (uint)(data.Length - pos - 8));
            int body = pos + 8;

            switch (id)
            {
                case "BMHD" when size >= 20:
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body));
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2));
                    planes = data[body + 8];
                    masking = data[body + 9];
                    compression = data[body + 10];
                    break;
                case "CMAP":
                    cmap = data.AsSpan(body, size).ToArray();
                    break;
                case "BODY":
                    bodyStart = body;
                    bodyLength = size;
                    break;
            }
            pos = body + size + (size & 1);
        }

        if (width == 0 || height == 0 || bodyStart < 0)
            throw new InvalidDataException("Missing BMHD or BODY chunk.");
        if (compression > 1)
            throw new InvalidDataException($"Unsupported IFF compression {compression}.");

        bool chunky = form == "PBM ";
        if (chunky ? planes != 8 : planes is < 1 or > 8 && planes != 24)
            throw new InvalidDataException($"Unsupported depth: {planes} bit planes.");

        int storedPlanes = chunky ? 1 : planes + (masking == 1 ? 1 : 0);
        int rowBytes = chunky ? (width + 1) & ~1 : (width + 15) / 16 * 2;
        byte[] raw = Unpack(data.AsSpan(bodyStart, bodyLength), compression == 1, rowBytes * storedPlanes * height);

        int[] palette = BuildPalette(cmap, planes);
        var argb = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowBytes * storedPlanes;
            for (int x = 0; x < width; x++)
            {
                if (chunky)
                {
                    argb[y * width + x] = palette[raw[rowStart + x]];
                    continue;
                }

                int value = 0;
                for (int p = 0; p < planes; p++)
                {
                    int bit = raw[rowStart + p * rowBytes + (x >> 3)] >> (7 - (x & 7)) & 1;
                    value |= bit << p;
                }
                argb[y * width + x] = planes == 24
                    ? unchecked((int)0xFF000000) | (value & 0xFF) << 16 | (value >> 8 & 0xFF) << 8 | (value >> 16 & 0xFF)
                    : palette[value];
            }
        }
        return SourceImage.FromArgb(width, height, argb);
    }

    private static string Tag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

    private static int[] BuildPalette(byte[]? cmap, int planes)
    {
        var palette = new int[256];
        int count = cmap is null ? 0 : cmap.Length / 3;
        for (int i = 0; i < palette.Length; i++)
        {
            if (i < count)
                palette[i] = unchecked((int)0xFF000000) | cmap![i * 3] << 16 | cmap[i * 3 + 1] << 8 | cmap[i * 3 + 2];
            else
            {
                // Without CMAP: grayscale levels spread over the image depth
                int max = (1 << Math.Min(planes, 8)) - 1;
                int gray = max == 0 ? 0 : Math.Min(255, i * 255 / max);
                palette[i] = unchecked((int)0xFF000000) | gray << 16 | gray << 8 | gray;
            }
        }
        return palette;
    }

    private static byte[] Unpack(ReadOnlySpan<byte> source, bool byteRun, int outputLength)
    {
        var output = new byte[outputLength];
        if (!byteRun)
        {
            source[..Math.Min(source.Length, outputLength)].CopyTo(output);
            return output;
        }

        int i = 0, o = 0;
        while (o < outputLength && i < source.Length)
        {
            sbyte n = (sbyte)source[i++];
            if (n >= 0)
            {
                int count = Math.Min(n + 1, Math.Min(source.Length - i, outputLength - o));
                source.Slice(i, count).CopyTo(output.AsSpan(o));
                i += n + 1;
                o += count;
            }
            else if (n != -128)
            {
                if (i >= source.Length)
                    break;
                int count = Math.Min(1 - n, outputLength - o);
                output.AsSpan(o, count).Fill(source[i++]);
                o += count;
            }
        }
        return output;
    }
}
