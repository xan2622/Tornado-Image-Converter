namespace TornadoImageConverter;

/// <summary>
/// DI Tornado BT2 image: raw 8-bit indexed pixels, row by row, with no header or palette.
/// The dimensions are deduced only from the file size.
/// </summary>
public sealed class Bt2Image
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Palette index,<c>Pixels[y * Width + x]</c>.</summary>
    public byte[] Pixels { get; }

    public Bt2Image(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static Bt2Image Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!TryGetDimensions(data.Length, out int width, out int height))
            throw new InvalidDataException($"Unsupported BT2 size: {data.Length:N0} bytes");
        return new Bt2Image(width, height, data);
    }

    public void Save(string path) => File.WriteAllBytes(path, Pixels);

    /// <summary>Pixels as 32-bit ARGB with an 8-bit RGB palette (768 bytes), index 0 optionally transparent.</summary>
    public int[] ToArgb(byte[] paletteRgb, bool transparentIndex0)
    {
        var lut = new int[256];
        for (int i = 0; i < lut.Length; i++)
        {
            uint alpha = transparentIndex0 && i == 0 ? 0u : 255u;
            lut[i] = unchecked((int)(alpha << 24 | (uint)paletteRgb[i * 3] << 16 | (uint)paletteRgb[i * 3 + 1] << 8 | paletteRgb[i * 3 + 2]));
        }

        var argb = new int[Pixels.Length];
        for (int i = 0; i < argb.Length; i++)
            argb[i] = lut[Pixels[i]];
        return argb;
    }

    /// <summary>
    /// Resamples with nearest neighbor: palette indices stay exact (no intermediate color).
    /// In Fit mode, the image keeps its proportions and the added bars use index 0.
    /// </summary>
    public Bt2Image Resize(int width, int height, SizeMismatch mode)
    {
        int destX = 0, destY = 0, destWidth = width, destHeight = height;
        if (mode == SizeMismatch.Fit)
        {
            double scale = Math.Min((double)width / Width, (double)height / Height);
            destWidth = Math.Max(1, (int)Math.Round(Width * scale));
            destHeight = Math.Max(1, (int)Math.Round(Height * scale));
            destX = (width - destWidth) / 2;
            destY = (height - destHeight) / 2;
        }

        var output = new byte[width * height];
        for (int y = 0; y < destHeight; y++)
        {
            int sourceRow = (int)((long)y * Height / destHeight) * Width;
            int outputRow = (destY + y) * width + destX;
            for (int x = 0; x < destWidth; x++)
                output[outputRow + x] = Pixels[sourceRow + (int)((long)x * Width / destWidth)];
        }
        return new Bt2Image(width, height, output);
    }

    public static bool TryGetDimensions(long size, out int width, out int height)
    {
        (width, height) = size switch
        {
            480_000 => (800, 600),  // SVGA mod panels
            64_000 => (320, 200),   // NAVIGPAN, EXTRAPAN, PREVIEW
            33_280 => (320, 104),   // original PILOTPAN
            12_800 => (320, 40),    // original PSIDEPAN / NSIDEPAN
            5_120 => (320, 16),     // original FRAMEPAN
            _ => (0, 0)
        };

        // Unknown size: only widths 800 and 320 exist in the game
        if (width == 0 && size > 0 && size <= int.MaxValue)
        {
            if (size % 800 == 0)
                (width, height) = (800, (int)(size / 800));
            else if (size % 320 == 0)
                (width, height) = (320, (int)(size / 320));
        }

        return width > 0;
    }
}
