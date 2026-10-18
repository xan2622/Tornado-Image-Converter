namespace TornadoImageConverter;

/// <summary>
/// Tornado PT2 image (digipics, title screens): 256-color 6-bit RGB palette (768 bytes), then
/// 320 x 200 pixels coded as 4-bit deltas (see UnpackDeltaPic in LIB8086/VGA_DRVR.ASM).
/// </summary>
/// <remarks>
/// Nibbles are read high nibble first, then low nibble. A nibble from -7 to 7 is added to the previous pixel
/// (modulo 256, starting at 0). Nibble 8 (that is -8) announces an absolute value: the next two nibbles,
/// the first being the low part of the pixel.
/// </remarks>
internal static class Pt2Image
{
    public const int Width = 320;
    public const int Height = 200;

    private const int PaletteSize = 256 * 3;

    public static bool LooksValid(long fileLength) => fileLength > PaletteSize;

    public static SourceImage Decode(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!LooksValid(data.Length))
            throw new InvalidDataException("File too small for a PT2 image.");

        var palette = new int[256];
        for (int i = 0; i < palette.Length; i++)
        {
            int r = Expand(data[i * 3]), g = Expand(data[i * 3 + 1]), b = Expand(data[i * 3 + 2]);
            palette[i] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
        }

        var argb = new int[Width * Height];
        int position = PaletteSize;
        bool lowNibble = false;
        int pixel = 0;

        int NextNibble()
        {
            if (position >= data.Length)
                throw new InvalidDataException("Truncated PT2 data.");
            int nibble = lowNibble ? data[position++] & 15 : data[position] >> 4;
            lowNibble = !lowNibble;
            return nibble;
        }

        for (int i = 0; i < argb.Length; i++)
        {
            int nibble = NextNibble();
            if (nibble == 8)
            {
                int low = NextNibble();
                int high = NextNibble();
                pixel = low | high << 4;
            }
            else
            {
                pixel = (pixel + (nibble > 7 ? nibble - 16 : nibble)) & 255;
            }
            argb[i] = palette[pixel];
        }
        return SourceImage.FromArgb(Width, Height, argb);
    }

    /// <summary>
    /// Writes an image as PT2. The image is brought to 320 x 200 (stretched), reduced to 256 6-bit colors
    /// sorted by luminance (which keeps the deltas small), then coded as 4-bit deltas.
    /// </summary>
    public static void Save(SourceImage source, string path)
    {
        if (source.Width != Width || source.Height != Height)
            source = source.Resize(Width, Height, SizeMismatch.Stretch);

        (int[] palette, byte[] pixels) = Quantize(source);

        var data = new List<byte>(PaletteSize + pixels.Length / 2);
        for (int i = 0; i < 256; i++)
        {
            int color = i < palette.Length ? palette[i] : 0;
            data.Add((byte)(color >> 12 & 63));
            data.Add((byte)(color >> 6 & 63));
            data.Add((byte)(color & 63));
        }

        var nibbles = new List<int>(pixels.Length + 64);
        int previous = 0;
        foreach (byte pixel in pixels)
        {
            int delta = (sbyte)(pixel - previous);
            if (delta is >= -7 and <= 7)
            {
                nibbles.Add(delta & 15);
            }
            else
            {
                nibbles.Add(8);
                nibbles.Add(pixel & 15);
                nibbles.Add(pixel >> 4);
            }
            previous = pixel;
        }
        if (nibbles.Count % 2 != 0)
            nibbles.Add(0);

        for (int i = 0; i < nibbles.Count; i += 2)
            data.Add((byte)(nibbles[i] << 4 | nibbles[i + 1]));

        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Palette (6-bit colors, r &lt;&lt; 12 | g &lt;&lt; 6 | b sorted by luminance) and index of each pixel.</summary>
    private static (int[] Palette, byte[] Pixels) Quantize(SourceImage source)
    {
        // 6-bit colors (alpha composited on black), with their frequency
        var keys = new int[source.Argb.Length];
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < keys.Length; i++)
        {
            int argb = source.Argb[i];
            int alpha = argb >> 24 & 0xFF;
            int r = (argb >> 16 & 0xFF) * alpha / 255 >> 2;
            int g = (argb >> 8 & 0xFF) * alpha / 255 >> 2;
            int b = (argb & 0xFF) * alpha / 255 >> 2;
            keys[i] = r << 12 | g << 6 | b;
            counts[keys[i]] = counts.GetValueOrDefault(keys[i]) + 1;
        }

        int[] colors = counts.Count <= 256 ? counts.Keys.ToArray() : MedianCut(counts, 256);
        Array.Sort(colors, (a, b) => Luminance(a).CompareTo(Luminance(b)));

        var lookup = new Dictionary<int, byte>();
        var pixels = new byte[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!lookup.TryGetValue(keys[i], out byte index))
                lookup[keys[i]] = index = Nearest(colors, keys[i]);
            pixels[i] = index;
        }
        return (colors, pixels);
    }

    private static int Luminance(int color) =>
        299 * (color >> 12 & 63) + 587 * (color >> 6 & 63) + 114 * (color & 63);

    private static byte Nearest(int[] colors, int key)
    {
        int r = key >> 12 & 63, g = key >> 6 & 63, b = key & 63;
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < colors.Length; i++)
        {
            int dr = r - (colors[i] >> 12 & 63), dg = g - (colors[i] >> 6 & 63), db = b - (colors[i] & 63);
            int distance = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
                if (distance == 0)
                    break;
            }
        }
        return (byte)best;
    }

    /// <summary>Frequency-weighted median cut: returns the average color of each resulting box.</summary>
    private static int[] MedianCut(Dictionary<int, int> counts, int target)
    {
        var boxes = new List<List<KeyValuePair<int, int>>> { counts.ToList() };
        while (boxes.Count < target)
        {
            // Box with the widest range on one channel, among those that still contain several colors
            int bestBox = -1, bestChannel = 0, bestRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                    continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    int shift = channel * 6;
                    int min = 63, max = 0;
                    foreach (var entry in boxes[i])
                    {
                        int v = entry.Key >> shift & 63;
                        min = Math.Min(min, v);
                        max = Math.Max(max, v);
                    }
                    if (max - min > bestRange)
                        (bestBox, bestChannel, bestRange) = (i, channel, max - min);
                }
            }
            if (bestBox < 0)
                break;

            List<KeyValuePair<int, int>> box = boxes[bestBox];
            int channelShift = bestChannel * 6;
            box.Sort((a, b) => (a.Key >> channelShift & 63).CompareTo(b.Key >> channelShift & 63));

            // Cut at the median point in number of pixels
            long total = box.Sum(e => (long)e.Value), running = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                running += box[i].Value;
                cut = i + 1;
                if (running * 2 >= total)
                    break;
            }
            boxes[bestBox] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }

        return boxes.Select(box =>
        {
            long weight = box.Sum(e => (long)e.Value);
            long r = 0, g = 0, b = 0;
            foreach (var entry in box)
            {
                r += (long)(entry.Key >> 12 & 63) * entry.Value;
                g += (long)(entry.Key >> 6 & 63) * entry.Value;
                b += (long)(entry.Key & 63) * entry.Value;
            }
            return (int)(r / weight) << 12 | (int)(g / weight) << 6 | (int)(b / weight);
        }).ToArray();
    }

    /// <summary>6 bits -> 8 bits by replicating the high bits (63 -> 255).</summary>
    private static int Expand(byte sixBit) => ((sixBit << 2) | (sixBit >> 4)) & 255;
}
