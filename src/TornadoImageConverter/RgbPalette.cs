namespace TornadoImageConverter;

/// <summary>
/// .RGB night palette: 6-bit R, G, B triplets (0 to 63, VGA DAC values), no header.
/// A file covers the last palette indices: 144 colors for PANEL_xy (112 to 255),
/// 80 for KNEES_xy (176 to 255, deduced from the size), 256 for a full palette.
/// </summary>
internal static class RgbPalette
{
    private const int MaxColors = 256;
    private const int Columns = 16;
    private const int CellSize = 16;
    private const int Gap = 1;
    private const int Background = unchecked((int)0xFF303030);

    public static bool LooksValid(ReadOnlySpan<byte> data, long length)
    {
        if (length == 0 || length % 3 != 0 || length > MaxColors * 3 || data.Length < length)
            return false;
        foreach (byte value in data[..(int)length])
        {
            if (value > 63)
                return false;
        }
        return true;
    }

    /// <summary>Index of the first color of the file and the colors as ARGB (6 bits expanded to 8 bits).</summary>
    public static (int FirstIndex, int[] Colors) Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!LooksValid(data, data.Length))
            throw new InvalidDataException("Not a valid RGB palette (6-bit triplets).");

        var colors = new int[data.Length / 3];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = unchecked((int)0xFF000000) | Expand(data[i * 3]) << 16 | Expand(data[i * 3 + 1]) << 8 | Expand(data[i * 3 + 2]);
        return (MaxColors - colors.Length, colors);
    }

    /// <summary>16-column grid: the color of index n is at row n / 16, column n % 16; only the useful rows are drawn.</summary>
    public static Bitmap Render(int firstIndex, int[] colors)
    {
        int firstRow = firstIndex / Columns;
        int rows = MaxColors / Columns - firstRow;
        int width = Columns * CellSize, height = rows * CellSize;

        var argb = new int[width * height];
        Array.Fill(argb, Background);

        for (int i = 0; i < colors.Length; i++)
        {
            int index = firstIndex + i;
            int left = index % Columns * CellSize, top = (index / Columns - firstRow) * CellSize;
            for (int y = 0; y < CellSize - Gap; y++)
            {
                for (int x = 0; x < CellSize - Gap; x++)
                    argb[(top + y) * width + left + x] = colors[i];
            }
        }
        return Bitmaps.FromArgb(width, height, argb);
    }

    // 6 bits -> 8 bits by replicating the high bits (63 -> 255)
    private static int Expand(byte sixBit) => (sixBit << 2) | (sixBit >> 4);
}
