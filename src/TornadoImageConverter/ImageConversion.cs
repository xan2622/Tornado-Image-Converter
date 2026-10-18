namespace TornadoImageConverter;

/// <summary>Destination image formats offered by the Explore tab.</summary>
internal enum TargetFormat
{
    Png,
    Bt2,
    Gfx,
    Pt2
}

/// <summary>Settings of a decoding (preview and conversion) and of an encoding.</summary>
/// <param name="Bt2SourcePalette">Palette of a source BT2 (null: automatic from the name).</param>
/// <param name="Bt2SourceTransparent">Index 0 of a source BT2 made transparent.</param>
/// <param name="Bt2Target">Settings when the destination is a BT2.</param>
/// <param name="BackupBt2">.bak copy of an existing BT2 before overwriting it.</param>
/// <param name="GfxSourcePalette">Palette of a source GFX (null: chosen from the name).</param>
/// <param name="GfxTargetPalette">16-color palette of a produced GFX (null: chosen from the output name).</param>
internal sealed record ConversionOptions(PaletteKind? Bt2SourcePalette, bool Bt2SourceTransparent, Bt2OutputSettings Bt2Target, bool BackupBt2, GfxPaletteKind? GfxSourcePalette, GfxPaletteKind? GfxTargetPalette)
{
    /// <summary>Effective palette of a source BT2: the chosen one, otherwise the one deduced from the name.</summary>
    public PaletteKind Bt2PaletteFor(string sourcePath) => Bt2SourcePalette ?? TornadoPalettes.ForFileName(sourcePath);

    /// <summary>Effective palette of a source GFX: the chosen one, otherwise the one deduced from the name.</summary>
    public GfxPaletteKind GfxPaletteFor(string sourcePath) => GfxSourcePalette ?? GfxPalettes.ForFileName(sourcePath);
}

/// <summary>Conversions between the game image formats (BT2, GFX, PT2), PNG, JPEG and LBM.</summary>
internal static class ImageConversion
{
    /// <summary>Formats a file can be converted to (empty: no useful conversion).</summary>
    public static TargetFormat[] TargetsFor(ViewKind kind, string extension) => kind switch
    {
        ViewKind.Bt2 => [TargetFormat.Png, TargetFormat.Bt2, TargetFormat.Gfx, TargetFormat.Pt2],
        ViewKind.Gfx => [TargetFormat.Png, TargetFormat.Bt2, TargetFormat.Pt2],
        ViewKind.Pt2 => [TargetFormat.Png, TargetFormat.Bt2, TargetFormat.Gfx],
        ViewKind.Iff => [TargetFormat.Png, TargetFormat.Bt2, TargetFormat.Pt2, TargetFormat.Gfx],
        ViewKind.Fnt => [TargetFormat.Png],
        ViewKind.Image when extension.Equals(".png", StringComparison.OrdinalIgnoreCase) =>
            [TargetFormat.Bt2, TargetFormat.Pt2, TargetFormat.Gfx],
        ViewKind.Image => [TargetFormat.Png, TargetFormat.Bt2, TargetFormat.Pt2, TargetFormat.Gfx],
        _ => []
    };

    public static string Label(TargetFormat format) => format switch
    {
        TargetFormat.Png => "PNG",
        TargetFormat.Bt2 => "BT2",
        TargetFormat.Gfx => "GFX",
        _ => "PT2"
    };

    /// <summary>Output name: upper case (DOS names) for the game formats, original name for PNG.</summary>
    public static string OutputFileName(string sourcePath, TargetFormat format)
    {
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        return format == TargetFormat.Png ? name + ".png" : name.ToUpperInvariant() + "." + Label(format);
    }

    public static void Convert(string sourcePath, ViewKind kind, TargetFormat target, string outputPath, ConversionOptions options)
    {
        if (kind == ViewKind.Bt2 && target == TargetFormat.Png)
        {
            // Exact round trip: indexed PNG with the Tornado palette
            Bt2Image image = Bt2Image.Load(sourcePath);
            using FileStream stream = File.Create(outputPath);
            PngWriter.WriteIndexed(stream, image.Width, image.Height, image.Pixels,
                TornadoPalettes.GetRgb8(options.Bt2PaletteFor(sourcePath)), options.Bt2SourceTransparent);
            return;
        }

        if (kind == ViewKind.Bt2 && target == TargetFormat.Bt2)
        {
            // Indices kept as is: only the size changes
            Bt2Output.Save(Bt2Output.Convert(Bt2Image.Load(sourcePath), sourcePath, options.Bt2Target), outputPath, options.BackupBt2);
            return;
        }

        SourceImage source = Decode(sourcePath, kind, options);
        switch (target)
        {
            case TargetFormat.Png:
                using (FileStream stream = File.Create(outputPath))
                    PngWriter.WriteRgba(stream, source.Width, source.Height, source.Argb);
                break;
            case TargetFormat.Bt2:
                Bt2Output.Save(Bt2Output.Convert(source, sourcePath, options.Bt2Target), outputPath, options.BackupBt2);
                break;
            case TargetFormat.Gfx:
                GfxImage.Save(source, outputPath, options.GfxTargetPalette);
                break;
            default:
                Pt2Image.Save(source, outputPath);
                break;
        }
    }

    /// <summary>Decodes a game image or a common image: used for both the preview and the conversion.</summary>
    public static SourceImage Decode(string path, ViewKind kind, ConversionOptions options)
    {
        switch (kind)
        {
            case ViewKind.Bt2:
                Bt2Image image = Bt2Image.Load(path);
                int[] argb = image.ToArgb(TornadoPalettes.GetRgb8(options.Bt2PaletteFor(path)), options.Bt2SourceTransparent);
                return SourceImage.FromArgb(image.Width, image.Height, argb);
            case ViewKind.Gfx:
                return GfxImage.Decode(path, options.GfxPaletteFor(path));
            case ViewKind.Pt2:
                return Pt2Image.Decode(path);
            case ViewKind.Iff:
                return IffImage.Decode(path);
            case ViewKind.Fnt:
                return FntImage.Decode(path);
            case ViewKind.Image:
                return SourceImage.Load(path);
            default:
                throw new NotSupportedException("This file type cannot be decoded as an image.");
        }
    }
}
