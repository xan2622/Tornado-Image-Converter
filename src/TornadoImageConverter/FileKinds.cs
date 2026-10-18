using System.Text;

namespace TornadoImageConverter;

/// <summary>How a file is displayed in the Explore tab.</summary>
internal enum ViewKind
{
    Bt2,
    Gfx,
    Pt2,
    Fnt,
    Palette,
    Image,
    Iff,
    Audio,
    Pdf,
    Text,
    Hex
}

/// <summary>
/// Recognition of Tornado files (inventory of the Steam installation: game, PDF manuals, FLAC soundtrack,
/// JPEG map) and text / hex rendering.
/// Formats without a decoder (DTA, IN, BIN, SAV, EXE...) are shown as hex.
/// </summary>
internal static class FileKinds
{
    private const int SniffLength = 4096;
    private const int MaxTextBytes = 2 * 1024 * 1024;
    private const int MaxHexBytes = 256 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".log", ".inf", ".cfg", ".bat", ".mis", ".conf", ".md", ".py" };

    /// <summary>Game binary formats without a decoder: label shown in the list.</summary>
    private static readonly Dictionary<string, string> BinaryDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bin"] = "Binary data (BIN: sound driver, music, terrain layer or demo)",
        [".dta"] = "Mission / save data (DTA)",
        [".in"] = "Game data exchange (IN)",
        [".sav"] = "Saved game data (SAV)",
        [".fnt"] = "Bitmap font (FNT)",
        [".rgb"] = "Night palette (RGB, 6-bit triplets)",
        [".exe"] = "DOS executable",
        [".com"] = "DOS executable"
    };

    private static readonly Encoding Dos437;

    static FileKinds()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Dos437 = Encoding.GetEncoding(437);
    }

    public static (ViewKind Kind, string Description) Classify(string path)
    {
        byte[] head;
        long length;
        using (FileStream stream = File.OpenRead(path))
        {
            length = stream.Length;
            head = new byte[(int)Math.Min(length, SniffLength)];
            stream.ReadExactly(head);
        }
        return Classify(path, head, length);
    }

    private static (ViewKind Kind, string Description) Classify(string path, byte[] head, long length)
    {
        string ext = Path.GetExtension(path);
        if (length == 0)
            return (ViewKind.Hex, "Empty file");

        if (head.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']))
            return (ViewKind.Image, "PNG image");

        if (head.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return (ViewKind.Image, "JPEG image");
        }

        if (Is4(head, 0, "%PDF") || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return (ViewKind.Pdf, "PDF document");

        if (Is4(head, 0, "fLaC") || ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
            return (ViewKind.Audio, "FLAC audio");

        if (head.Length >= 12 && Is4(head, 0, "FORM") && (Is4(head, 8, "PBM ") || Is4(head, 8, "ILBM")))
            return (ViewKind.Iff, "IFF/LBM image (game screenshot)");

        if (ext.Equals(".gfx", StringComparison.OrdinalIgnoreCase) && GfxImage.TryGetSize(head, length, out int gw, out int gh))
            return (ViewKind.Gfx, $"GFX image {gw} x {gh} (16 colors)");

        if (ext.Equals(".pt2", StringComparison.OrdinalIgnoreCase) && Pt2Image.LooksValid(length))
            return (ViewKind.Pt2, $"PT2 image {Pt2Image.Width} x {Pt2Image.Height} (256 colors)");

        if (ext.Equals(".fnt", StringComparison.OrdinalIgnoreCase) && FntImage.TryDetect(head, length, out FntImage.Layout fnt))
            return (ViewKind.Fnt, $"Bitmap font {FntImage.PixelWidth(fnt)} x 16, {FntImage.GlyphCount(fnt)} glyphs");

        if (ext.Equals(".rgb", StringComparison.OrdinalIgnoreCase) && RgbPalette.LooksValid(head, length))
            return (ViewKind.Palette, $"Night palette, {length / 3} colors (RGB, 6-bit triplets)");

        if (ext.Equals(".bt2", StringComparison.OrdinalIgnoreCase) && Bt2Image.TryGetDimensions(length, out int w, out int h))
            return (ViewKind.Bt2, $"BT2 image {w} x {h}");

        if (TextExtensions.Contains(ext) || !BinaryDescriptions.ContainsKey(ext) && LooksLikeText(head))
            return (ViewKind.Text, "Text");

        return (ViewKind.Hex, BinaryDescriptions.GetValueOrDefault(ext, "Binary data"));
    }

    private static bool Is4(byte[] data, int offset, string tag) =>
        data.Length >= offset + 4 && Encoding.ASCII.GetString(data, offset, 4) == tag;

    /// <summary>Probably text: no null byte and very few control characters.</summary>
    private static bool LooksLikeText(byte[] head)
    {
        int control = 0;
        foreach (byte b in head)
        {
            if (b == 0)
                return false;
            if (b < 32 && b is not (9 or 10 or 12 or 13 or 26 or 27))
                control++;
        }
        return control * 50 <= head.Length;
    }

    public static string ReadText(string path, out bool truncated)
    {
        byte[] data = ReadHead(path, MaxTextBytes, out truncated);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            text = Dos437.GetString(data);  // original DOS files
        }
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }

    public static string HexDump(string path, out bool truncated)
    {
        byte[] data = ReadHead(path, MaxHexBytes, out truncated);
        var sb = new StringBuilder(data.Length * 4);
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            sb.Append(offset.ToString("X8")).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                sb.Append(offset + i < data.Length ? data[offset + i].ToString("X2") : "  ").Append(' ');
                if (i == 7)
                    sb.Append(' ');
            }
            sb.Append(' ');
            for (int i = 0; i < 16 && offset + i < data.Length; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static byte[] ReadHead(string path, int max, out bool truncated)
    {
        using FileStream stream = File.OpenRead(path);
        truncated = stream.Length > max;
        var data = new byte[(int)Math.Min(stream.Length, max)];
        stream.ReadExactly(data);
        return data;
    }
}
