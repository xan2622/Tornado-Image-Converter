namespace TornadoImageConverter;

/// <summary>Size of the BT2 produced.</summary>
public enum TargetSize
{
    /// <summary>SVGA mod: 800x600, except PREVIEW at 320x200.</summary>
    AutoSvgaMod,

    /// <summary>Original game: size of each original file (PILOTPAN 320x104, etc.).</summary>
    AutoOriginalGame,

    Svga800x600,
    Vga320x200,
    KeepImageSize
}

/// <summary>BT2 encoding settings: palette (null: automatic from the name), allowed colors, dithering and size.</summary>
public sealed record Bt2OutputSettings(PaletteKind? Palette, ColorRange Range, bool Dithering, TargetSize Target, SizeMismatch Mismatch);

/// <summary>Production of a BT2 from a decoded image: resizing to the target size, encoding, writing with backup.</summary>
public static class Bt2Output
{
    private static readonly Dictionary<string, Size> OriginalSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PILOTPAN"] = new Size(320, 104),
        ["PSIDEPAN"] = new Size(320, 40),
        ["NSIDEPAN"] = new Size(320, 40),
        ["FRAMEPAN"] = new Size(320, 16),
        ["NAVIGPAN"] = new Size(320, 200),
        ["EXTRAPAN"] = new Size(320, 200),
        ["PREVIEW"] = new Size(320, 200)
    };

    public static Size ResolveTargetSize(TargetSize target, string fileName, Size imageSize)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return target switch
        {
            TargetSize.AutoSvgaMod => name.StartsWith("PREVIEW", StringComparison.OrdinalIgnoreCase) ? new Size(320, 200) : new Size(800, 600),
            TargetSize.AutoOriginalGame => OriginalSizes.GetValueOrDefault(name, new Size(320, 200)),
            TargetSize.Svga800x600 => new Size(800, 600),
            TargetSize.Vga320x200 => new Size(320, 200),
            _ => imageSize
        };
    }

    /// <param name="sourcePath">Original path: its name decides the automatic palette and size.</param>
    public static Bt2Image Convert(SourceImage source, string sourcePath, Bt2OutputSettings settings)
    {
        Size target = ResolveTargetSize(settings.Target, sourcePath, new Size(source.Width, source.Height));

        if (target.Width != source.Width || target.Height != source.Height)
        {
            if (settings.Mismatch == SizeMismatch.Skip)
                throw new InvalidDataException($"Image is {source.Width} x {source.Height}, expected {target.Width} x {target.Height}");
            source = source.Resize(target.Width, target.Height, settings.Mismatch);
        }

        PaletteKind palette = settings.Palette ?? TornadoPalettes.ForFileName(sourcePath);
        return Bt2Encoder.Encode(source, new Bt2EncodeOptions(palette, settings.Range, settings.Dithering));
    }

    /// <summary>
    /// BT2 to BT2: resizes to the target size without touching the palette indices, so no palette, colors or dithering.
    /// </summary>
    public static Bt2Image Convert(Bt2Image source, string sourcePath, Bt2OutputSettings settings)
    {
        Size target = ResolveTargetSize(settings.Target, sourcePath, new Size(source.Width, source.Height));
        if (target.Width == source.Width && target.Height == source.Height)
            return source;
        if (settings.Mismatch == SizeMismatch.Skip)
            throw new InvalidDataException($"Image is {source.Width} x {source.Height}, expected {target.Width} x {target.Height}");
        return source.Resize(target.Width, target.Height, settings.Mismatch);
    }

    /// <summary>Writes the BT2; first backs up the existing file as .bak (without overwriting an existing backup).</summary>
    public static void Save(Bt2Image image, string bt2Path, bool backupExisting)
    {
        if (backupExisting && File.Exists(bt2Path))
        {
            string backup = bt2Path + ".bak";
            if (!File.Exists(backup))
                File.Copy(bt2Path, backup);
        }
        image.Save(bt2Path);
    }
}
