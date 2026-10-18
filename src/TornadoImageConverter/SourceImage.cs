using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WpfImaging = System.Windows.Media.Imaging;
using WpfMedia = System.Windows.Media;

namespace TornadoImageConverter;

/// <summary>Behavior when the image size differs from the target BT2 size.</summary>
public enum SizeMismatch
{
    Stretch,
    Fit,
    Skip
}

/// <summary>
/// Source image to convert to BT2: ARGB pixels, plus the original indices and palette
/// if the image is 8-bit indexed (to keep the exact indices in a round trip).
/// </summary>
public sealed class SourceImage
{
    public int Width { get; }
    public int Height { get; }
    public int[] Argb { get; }
    public byte[]? Indices { get; }
    public int[]? PaletteArgb { get; }

    private SourceImage(int width, int height, int[] argb, byte[]? indices, int[]? paletteArgb)
    {
        Width = width;
        Height = height;
        Argb = argb;
        Indices = indices;
        PaletteArgb = paletteArgb;
    }

    /// <summary>True-color image from another decoder (GFX, PT2, BT2...).</summary>
    public static SourceImage FromArgb(int width, int height, int[] argb) => new(width, height, argb, null, null);

    /// <summary>
    /// Decodes the image through WIC (WPF), which applies neither gamma nor color profile
    /// and exposes the palette of an indexed PNG as is.
    /// </summary>
    public static SourceImage Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var decoder = WpfImaging.BitmapDecoder.Create(stream,
            WpfImaging.BitmapCreateOptions.PreservePixelFormat | WpfImaging.BitmapCreateOptions.IgnoreColorProfile,
            WpfImaging.BitmapCacheOption.OnLoad);
        WpfImaging.BitmapSource frame = decoder.Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;

        if (frame.Format == WpfMedia.PixelFormats.Indexed8 && frame.Palette is { } palette)
        {
            var indices = new byte[width * height];
            frame.CopyPixels(indices, width, 0);

            var paletteArgb = new int[256];
            Array.Fill(paletteArgb, unchecked((int)0xFF000000));
            for (int i = 0; i < palette.Colors.Count && i < 256; i++)
            {
                WpfMedia.Color c = palette.Colors[i];
                paletteArgb[i] = c.A << 24 | c.R << 16 | c.G << 8 | c.B;
            }

            var argb = new int[indices.Length];
            for (int i = 0; i < argb.Length; i++)
                argb[i] = paletteArgb[indices[i]];

            return new SourceImage(width, height, argb, indices, paletteArgb);
        }

        var converted = new WpfImaging.FormatConvertedBitmap(frame, WpfMedia.PixelFormats.Bgra32, null, 0);
        var pixels = new int[width * height];
        converted.CopyPixels(pixels, width * 4, 0);
        return new SourceImage(width, height, pixels, null, null);
    }

    /// <summary>Resizes (bicubic); in Fit mode, the added bars are transparent (index 0).</summary>
    public SourceImage Resize(int width, int height, SizeMismatch mode)
    {
        Rectangle dest = new(0, 0, width, height);
        if (mode == SizeMismatch.Fit)
        {
            double scale = Math.Min((double)width / Width, (double)height / Height);
            int w = Math.Max(1, (int)Math.Round(Width * scale));
            int h = Math.Max(1, (int)Math.Round(Height * scale));
            dest = new Rectangle((width - w) / 2, (height - h) / 2, w, h);
        }

        using Bitmap source = Bitmaps.FromArgb(Width, Height, Argb);
        using var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(target))
        using (var attributes = new ImageAttributes())
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);  // avoids darkened edges
            g.DrawImage(source, dest, 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
        }

        return new SourceImage(width, height, Bitmaps.ToArgb(target), null, null);
    }
}
