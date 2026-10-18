namespace TornadoImageConverter;

/// <summary>Palette indices allowed for approximated colors.</summary>
public enum ColorRange
{
    /// <summary>
    /// Colors used by the original panels: 0 (black / background), 142, 143, 160..175 (CRT screens)
    /// and 176..255. All of them have night variants (PANEL_xy.RGB covers 112..255).
    /// </summary>
    PanelColors,

    /// <summary>The whole palette (colors 24..175 change with night, fog, etc.).</summary>
    FullPalette
}

public sealed record Bt2EncodeOptions(PaletteKind Palette, ColorRange Range, bool Dithering);

/// <summary>Converts a true-color (or indexed) image into BT2 pixels indexed on the Tornado palette.</summary>
public static class Bt2Encoder
{
    private const int AlphaThreshold = 128;

    public static Bt2Image Encode(SourceImage source, Bt2EncodeOptions options)
    {
        byte[] rgb = TornadoPalettes.GetRgb8(options.Palette);
        int lastIndex = TornadoPalettes.LastUsableIndex(options.Palette);
        // Preview mode has no night: the whole palette is safe there
        ColorRange range = options.Palette == PaletteKind.Preview ? ColorRange.FullPalette : options.Range;
        int[] candidates = BuildCandidates(rgb, lastIndex, range);

        var exactColors = new Dictionary<int, byte>();
        foreach (int index in candidates)
            exactColors.TryAdd(Pack(rgb, index), (byte)index);

        int width = source.Width, height = source.Height;
        var output = new byte[width * height];
        var nearestCache = new Dictionary<int, byte>();

        // Floyd-Steinberg error diffusion: current and next row, with a margin column on each side
        float[]? errorRow = options.Dithering ? new float[(width + 2) * 3] : null;
        float[]? errorNext = options.Dithering ? new float[(width + 2) * 3] : null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int argb = source.Argb[i];

                if ((uint)argb >> 24 < AlphaThreshold)
                {
                    output[i] = 0;
                    continue;
                }

                // Indexed PNG whose color matches the same Tornado palette entry: index kept as is
                if (source.Indices is { } indices && source.PaletteArgb is { } sourcePalette)
                {
                    int sourceIndex = indices[i];
                    if (sourceIndex <= lastIndex && (sourcePalette[sourceIndex] & 0xFFFFFF) == Pack(rgb, sourceIndex))
                    {
                        output[i] = (byte)sourceIndex;
                        continue;
                    }
                }

                int color = argb & 0xFFFFFF;
                if (exactColors.TryGetValue(color, out byte exactIndex))
                {
                    output[i] = exactIndex;
                    continue;
                }

                int r = (color >> 16) & 0xFF, g = (color >> 8) & 0xFF, b = color & 0xFF;

                if (errorRow is null || errorNext is null)
                {
                    if (!nearestCache.TryGetValue(color, out byte nearest))
                    {
                        nearest = FindNearest(r, g, b, rgb, candidates);
                        nearestCache[color] = nearest;
                    }
                    output[i] = nearest;
                    continue;
                }

                int e = (x + 1) * 3;
                r = ClampByte(r + errorRow[e]);
                g = ClampByte(g + errorRow[e + 1]);
                b = ClampByte(b + errorRow[e + 2]);

                byte index = FindNearest(r, g, b, rgb, candidates);
                output[i] = index;

                float er = r - rgb[index * 3], eg = g - rgb[index * 3 + 1], eb = b - rgb[index * 3 + 2];
                Diffuse(errorRow, e + 3, er, eg, eb, 7f / 16);
                Diffuse(errorNext, e - 3, er, eg, eb, 3f / 16);
                Diffuse(errorNext, e, er, eg, eb, 5f / 16);
                Diffuse(errorNext, e + 3, er, eg, eb, 1f / 16);
            }

            if (errorRow is not null && errorNext is not null)
            {
                (errorRow, errorNext) = (errorNext, errorRow);
                Array.Clear(errorNext);
            }
        }

        return new Bt2Image(width, height, output);
    }

    /// <summary>
    /// Candidate indices, once per color. The order gives priority to 0 then to 176..255
    /// in case of duplicates (e.g. black at 0, 29, 137...; 142 and 173 identical).
    /// </summary>
    private static int[] BuildCandidates(byte[] rgb, int lastIndex, ColorRange range)
    {
        IEnumerable<int> panel = Enumerable.Range(TornadoPalettes.PanelFirstColor, lastIndex - TornadoPalettes.PanelFirstColor + 1);
        IEnumerable<int> order = range == ColorRange.PanelColors
            ? [0, .. panel, 142, 143, .. Enumerable.Range(160, 16)]
            : [0, .. panel, .. Enumerable.Range(1, TornadoPalettes.PanelFirstColor - 1)];

        var seen = new HashSet<int>();
        return order.Where(index => seen.Add(Pack(rgb, index))).ToArray();
    }

    /// <summary>"redmean" distance: simple perceptual approximation of the difference between two colors.</summary>
    private static byte FindNearest(int r, int g, int b, byte[] rgb, int[] candidates)
    {
        int best = candidates[0];
        long bestDistance = long.MaxValue;
        foreach (int index in candidates)
        {
            int pr = rgb[index * 3], pg = rgb[index * 3 + 1], pb = rgb[index * 3 + 2];
            int mean = (r + pr) >> 1;
            int dr = r - pr, dg = g - pg, db = b - pb;
            long distance = (((512 + mean) * dr * dr) >> 8) + 4L * dg * dg + (((767 - mean) * db * db) >> 8);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
                if (distance == 0)
                    break;
            }
        }
        return (byte)best;
    }

    private static void Diffuse(float[] row, int offset, float er, float eg, float eb, float weight)
    {
        row[offset] += er * weight;
        row[offset + 1] += eg * weight;
        row[offset + 2] += eb * weight;
    }

    private static int ClampByte(float value) => value <= 0 ? 0 : value >= 255 ? 255 : (int)(value + 0.5f);

    private static int Pack(byte[] rgb, int index) => rgb[index * 3] << 16 | rgb[index * 3 + 1] << 8 | rgb[index * 3 + 2];
}
