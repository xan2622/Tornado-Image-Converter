using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace TornadoImageConverter;

/// <summary>16-color sets that AMP.EXE loads into the VGA DAC depending on the screen displayed.</summary>
internal enum GfxPaletteKind
{
    /// <summary>Menus and grayscale photos (game palettes 1 to 3).</summary>
    Menu,

    /// <summary>Debriefing: blue-gray tinted photos (palettes 4 to 8).</summary>
    Debrief,

    /// <summary>Warm brown palette (palette 0, desert theme).</summary>
    Brown,

    /// <summary>Warm olive palette (palette 0, other theme).</summary>
    Olive,

    /// <summary>Gray-green palette (palette 9).</summary>
    Green
}

/// <summary>
/// Palettes of GFX images. GFX files contain none: AMP.EXE defines them in its code
/// (function that calls SetPalette(index, r, g, b) with 6-bit DAC values), 10 palette numbers
/// grouped into 5 color sets.
/// </summary>
internal static partial class GfxPalettes
{
    // 6-bit DAC values (r, g, b) of the 16 colors, extracted from AMP.EXE
    private static readonly byte[][] Dac =
    [
        // Menu: grayscale (cases 1 to 3)
        [0,0,0, 7,7,7, 9,12,12, 15,16,16, 18,20,21, 23,25,26, 12,16,23, 33,35,37, 37,40,42, 42,45,48, 28,30,31, 52,56,59, 47,50,52, 63,47,10, 36,41,49, 56,61,63],
        // Debrief: blue-gray (cases 4 to 8)
        [0,0,0, 17,21,29, 27,32,41, 26,7,0, 12,16,23, 22,28,37, 20,24,33, 14,18,26, 31,37,46, 39,44,52, 45,49,56, 42,46,54, 48,52,59, 63,46,0, 36,41,49, 63,63,63],
        // Warm brown (case 0)
        [0,0,0, 26,22,19, 20,13,6, 26,7,0, 12,16,23, 28,17,0, 20,24,33, 16,16,16, 31,37,46, 54,46,38, 48,33,20, 39,0,0, 48,52,59, 62,37,0, 36,41,49, 63,63,63],
        // Warm olive (case 0)
        [0,0,0, 27,24,21, 18,21,5, 26,7,0, 12,16,23, 28,17,0, 20,24,33, 16,16,16, 31,37,46, 54,49,40, 35,40,10, 39,0,0, 48,52,59, 62,37,0, 36,41,49, 63,63,63],
        // Gray-green (case 9)
        [0,0,0, 18,24,28, 20,20,20, 26,7,0, 12,15,23, 40,22,22, 20,24,33, 30,39,11, 31,37,46, 40,43,22, 48,47,34, 54,50,43, 48,52,59, 63,46,0, 36,41,49, 63,63,63]
    ];

    [GeneratedRegex(@"^(DB\d+|DEBRIEF|DUALDEB)$", RegexOptions.IgnoreCase)]
    private static partial Regex DebriefName();

    /// <summary>Presumed palette from the name: the debriefing screens, otherwise the menus one.</summary>
    public static GfxPaletteKind ForFileName(string path) =>
        DebriefName().IsMatch(Path.GetFileNameWithoutExtension(path)) ? GfxPaletteKind.Debrief : GfxPaletteKind.Menu;

    public static string DisplayName(GfxPaletteKind kind) => kind switch
    {
        GfxPaletteKind.Debrief => "Debrief (blue-gray)",
        GfxPaletteKind.Brown => "Warm menu (brown)",
        GfxPaletteKind.Olive => "Warm menu (olive)",
        GfxPaletteKind.Green => "Gray-green",
        _ => "Menu (grayscale)"
    };

    /// <summary>The 16 colors as 8-bit RGB (0xRRGGBB).</summary>
    public static int[] Rgb(GfxPaletteKind kind)
    {
        byte[] dac = Dac[(int)kind];
        var rgb = new int[16];
        for (int i = 0; i < 16; i++)
            rgb[i] = Expand(dac[i * 3]) << 16 | Expand(dac[i * 3 + 1]) << 8 | Expand(dac[i * 3 + 2]);
        return rgb;
    }

    // 6 bits -> 8 bits by replicating the high bits (63 -> 255)
    private static int Expand(byte sixBit) => (sixBit << 2) | (sixBit >> 4);
}

/// <summary>
/// GFX image of Tornado menus (AMP.EXE): 4-byte header (width in bytes, height, 16 bits each),
/// then 4 bit planes per row (planes 0 to 3, width / 8 bytes each) in 16 colors.
/// </summary>
internal static class GfxImage
{
    private const int HeaderSize = 4;
    private const int Planes = 4;

    public static bool TryGetSize(ReadOnlySpan<byte> head, long fileLength, out int width, out int height)
    {
        width = height = 0;
        if (head.Length < HeaderSize)
            return false;

        int widthBytes = BinaryPrimitives.ReadUInt16LittleEndian(head);
        int rows = BinaryPrimitives.ReadUInt16LittleEndian(head[2..]);
        if (widthBytes == 0 || rows == 0 || fileLength != HeaderSize + (long)widthBytes * rows * Planes)
            return false;

        (width, height) = (widthBytes * 8, rows);
        return true;
    }

    /// <summary>Writes the image as GFX: closest color of each pixel in the output file palette, width rounded up to 8 pixels.</summary>
    /// <param name="palette">Output palette (null: chosen from the name of the produced file).</param>
    public static void Save(SourceImage source, string path, GfxPaletteKind? palette)
    {
        int[] colors = GfxPalettes.Rgb(palette ?? GfxPalettes.ForFileName(path));
        int widthBytes = (source.Width + 7) / 8;
        if (widthBytes > ushort.MaxValue || source.Height > ushort.MaxValue)
            throw new InvalidDataException("Image too large for a GFX file.");

        var data = new byte[HeaderSize + widthBytes * source.Height * Planes];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)widthBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)source.Height);

        var cache = new Dictionary<int, int>();
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int argb = source.Argb[y * source.Width + x];
                if (!cache.TryGetValue(argb, out int color))
                    cache[argb] = color = Nearest(argb, colors);

                for (int plane = 0; plane < Planes; plane++)
                {
                    if ((color >> plane & 1) != 0)
                        data[HeaderSize + (y * Planes + plane) * widthBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
        File.WriteAllBytes(path, data);
    }

    private static int Nearest(int argb, int[] palette)
    {
        if ((uint)argb >> 24 < 128)
            return 0;

        int r = argb >> 16 & 0xFF, g = argb >> 8 & 0xFF, b = argb & 0xFF;
        int best = 0;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int pr = palette[i] >> 16 & 0xFF, pg = palette[i] >> 8 & 0xFF, pb = palette[i] & 0xFF;
            int mean = (r + pr) >> 1;
            int dr = r - pr, dg = g - pg, db = b - pb;
            // "redmean" distance, like the BT2 encoder
            long distance = (((512 + mean) * dr * dr) >> 8) + 4L * dg * dg + (((767 - mean) * db * db) >> 8);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    public static SourceImage Decode(string path, GfxPaletteKind palette)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!TryGetSize(data, data.Length, out int width, out int height))
            throw new InvalidDataException("Not a valid GFX file (size does not match its header).");

        int[] colors = GfxPalettes.Rgb(palette);
        int widthBytes = width / 8;
        var argb = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = HeaderSize + y * widthBytes * Planes;
            for (int x = 0; x < width; x++)
            {
                int color = 0;
                for (int plane = 0; plane < Planes; plane++)
                    color |= (data[row + plane * widthBytes + (x >> 3)] >> (7 - (x & 7)) & 1) << plane;
                argb[y * width + x] = unchecked((int)0xFF000000) | colors[color];
            }
        }
        return SourceImage.FromArgb(width, height, argb);
    }
}
