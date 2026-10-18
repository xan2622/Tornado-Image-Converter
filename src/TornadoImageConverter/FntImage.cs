namespace TornadoImageConverter;

/// <summary>
/// Tornado FNT fonts (AMP.EXE), both5 632 bytes, no header, 1 bit per pixel, 16 rows per glyph.
/// FONT.FNT: 352 glyphs of 8 x 16 (16 bytes, one per row), indexed by ASCII code (set close to CP437).
/// BIGFONT.FNT: 176 glyphs of 16 x 16 (32 bytes: 16 bytes of the left half, then 16 of the right half),
/// indexed by ASCII code.
/// </summary>
internal static class FntImage
{
    public const int FileSize = 5632;

    private const int Rows = 16;
    private const int Columns = 16;
    private const int CellGap = 2;

    private const int Background = unchecked((int)0xFF181818);
    private const int Grid = unchecked((int)0xFF3A3A3A);
    private const int Ink = unchecked((int)0xFFFFFFFF);

    public enum Layout
    {
        Narrow,
        Wide
    }

    public static int PixelWidth(Layout layout) => layout == Layout.Wide ? 16 : 8;

    public static int GlyphCount(Layout layout) => layout == Layout.Wide ? FileSize / 32 : FileSize / 16;

    /// <summary>
    /// In BIGFONT, each glyph takes 32 bytes and its right half is almost empty: read in blocks of
    /// 16 bytes, every other cell (the odd ones) is therefore almost empty. In FONT, all cells are filled.
    /// </summary>
    public static bool TryDetect(ReadOnlySpan<byte> head, long fileLength, out Layout layout)
    {
        layout = Layout.Narrow;
        if (fileLength != FileSize || head.Length < 176 * 16)
            return false;

        int even = 0, odd = 0;
        for (int cell = 0; cell < 176; cell++)
        {
            if (head.Slice(cell * 16, 16).IndexOfAnyExcept((byte)0) < 0)
                continue;
            if (cell % 2 == 0)
                even++;
            else
                odd++;
        }
        layout = odd * 3 < even ? Layout.Wide : Layout.Narrow;
        return true;
    }

    /// <summary>Sheet of all glyphs, 16 per row, white on a dark background.</summary>
    public static SourceImage Decode(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!TryDetect(data, data.Length, out Layout layout))
            throw new InvalidDataException($"Unexpected FNT size: {data.Length:N0} bytes (expected {FileSize:N0}).");

        int glyphWidth = PixelWidth(layout);
        int count = GlyphCount(layout);
        int sheetRows = (count + Columns - 1) / Columns;
        int cellWidth = glyphWidth + CellGap, cellHeight = Rows + CellGap;
        int width = Columns * cellWidth + CellGap, height = sheetRows * cellHeight + CellGap;

        var argb = new int[width * height];
        Array.Fill(argb, Grid);
        for (int glyph = 0; glyph < count; glyph++)
        {
            int originX = CellGap + glyph % Columns * cellWidth;
            int originY = CellGap + glyph / Columns * cellHeight;
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < glyphWidth; x++)
                {
                    bool on = layout == Layout.Wide
                        ? Bit(data[glyph * 32 + x / 8 * 16 + y], x % 8)
                        : Bit(data[glyph * 16 + y], x);
                    argb[(originY + y) * width + originX + x] = on ? Ink : Background;
                }
            }
        }
        return SourceImage.FromArgb(width, height, argb);
    }

    private static bool Bit(byte value, int x) => (value >> (7 - x) & 1) != 0;
}
