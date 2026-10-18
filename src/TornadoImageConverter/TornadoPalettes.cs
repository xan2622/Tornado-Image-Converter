namespace TornadoImageConverter;

/// <summary>Color set 176..255 overlaid on the base palette depending on the view.</summary>
public enum PaletteKind
{
    Standard,
    LookDown,
    Preview
}

/// <summary>
/// Tornado VGA palettes. Colors 0..175 are fixed, colors 176..255 depend
/// on the panel displayed (see VISUAL/PALETTES.ASM).
/// </summary>
public static partial class TornadoPalettes
{
    public const int ColorCount = 256;
    public const int PanelFirstColor = 176;

    /// <summary>Last significant index: in preview mode, 208..255 are unused (black).</summary>
    public static int LastUsableIndex(PaletteKind kind) => kind == PaletteKind.Preview ? 207 : 255;

    public static PaletteKind ForFileName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();
        if (name.StartsWith("EXTRAPAN", StringComparison.Ordinal))
            return PaletteKind.LookDown;
        if (name.StartsWith("PREVIEW", StringComparison.Ordinal))
            return PaletteKind.Preview;
        return PaletteKind.Standard;
    }

    public static string DisplayName(this PaletteKind kind) => kind switch
    {
        PaletteKind.LookDown => "Look down",
        PaletteKind.Preview => "Preview",
        _ => "Standard"
    };

    /// <summary>Full palette as 8-bit RGB (768 bytes).</summary>
    public static byte[] GetRgb8(PaletteKind kind)
    {
        var rgb = new byte[ColorCount * 3];
        Base6.CopyTo(rgb);

        ReadOnlySpan<byte> overlay = kind switch
        {
            PaletteKind.LookDown => Panel2_6,
            PaletteKind.Preview => Preview6,
            _ => Panel1_6
        };
        overlay.CopyTo(rgb.AsSpan(PanelFirstColor * 3));

        // 6 bits -> 8 bits by replicating the high bits (63 -> 255)
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = (byte)((rgb[i] << 2) | (rgb[i] >> 4));

        return rgb;
    }
}
