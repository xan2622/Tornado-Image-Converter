namespace TornadoImageConverter;

/// <summary>How a file is displayed in the preview of the Explore tab.</summary>
internal enum ViewMode
{
    Auto,
    Hex
}

/// <summary>
/// Display and conversion options of a file extension (one tab of the Explore tab).
/// The options offered depend on the type of the source file (what is needed to display and decode it)
/// and on the chosen destination format (what is needed to encode it): nothing useless is shown.
/// </summary>
internal sealed class ExtensionOptions : UserControl
{
    private readonly ComboBox cboView = Ui.DropDown<ViewMode>([new("Text", ViewMode.Auto), new("Hex dump", ViewMode.Hex)], 130);
    private readonly ComboBox cboPalette = Ui.DropDown(Ui.PaletteChoices(), 190);
    private readonly ComboBox cboGfxPalette = Ui.DropDown<GfxPaletteKind?>(
    [
        new("Auto (by file name)", null),
        new(GfxPalettes.DisplayName(GfxPaletteKind.Menu), GfxPaletteKind.Menu),
        new(GfxPalettes.DisplayName(GfxPaletteKind.Debrief), GfxPaletteKind.Debrief),
        new(GfxPalettes.DisplayName(GfxPaletteKind.Brown), GfxPaletteKind.Brown),
        new(GfxPalettes.DisplayName(GfxPaletteKind.Olive), GfxPaletteKind.Olive),
        new(GfxPalettes.DisplayName(GfxPaletteKind.Green), GfxPaletteKind.Green)
    ], 190);
    private readonly CheckBox chkTransparent = new() { Text = "Index 0 transparent", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly ComboBox cboColors = Ui.DropDown<ColorRange>(
    [
        new("Panel colors (night-safe)", ColorRange.PanelColors),
        new("Full palette (0-255)", ColorRange.FullPalette)
    ], 190);
    private readonly CheckBox chkDither = new() { Text = "Dithering", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly ComboBox cboTarget = Ui.DropDown<TargetSize>(
    [
        new("Auto (SVGA mod)", TargetSize.AutoSvgaMod),
        new("Auto (original game)", TargetSize.AutoOriginalGame),
        new("800 x 600", TargetSize.Svga800x600),
        new("320 x 200", TargetSize.Vga320x200),
        new("Keep image size", TargetSize.KeepImageSize)
    ], 160);
    private readonly ComboBox cboMismatch = Ui.DropDown<SizeMismatch>(
    [
        new("Stretch to target size", SizeMismatch.Stretch),
        new("Fit (keep aspect ratio)", SizeMismatch.Fit),
        new("Skip file", SizeMismatch.Skip)
    ], 170);
    private readonly CheckBox chkBackup = new() { Text = "Back up existing BT2 (.bak)", AutoSize = true, Checked = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly ToolTip toolTip = new() { AutoPopDelay = 20000 };

    private readonly Label lblView = Ui.Caption("View as:");
    private readonly Label lblPalette = Ui.Caption("Palette:");
    private readonly Label lblGfxPalette = Ui.Caption("GFX palette:");
    private readonly Label lblColors = Ui.Caption("Colors:");
    private readonly Label lblTargetSize = Ui.Caption("Target size:");
    private readonly Label lblMismatch = Ui.Caption("If size differs:");
    private readonly Label lblNone = Ui.Caption("No option for this file type.");
    private readonly FlowLayoutPanel rows = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(4, 4, 0, 0) };
    private readonly FlowLayoutPanel sourceRow = NewRow();
    private readonly FlowLayoutPanel bt2ColorRow = NewRow();
    private readonly FlowLayoutPanel bt2SizeRow = NewRow();
    private readonly FlowLayoutPanel gfxTargetRow = NewRow();
    private readonly FlowLayoutPanel noneRow = NewRow();

    private readonly bool isBt2;
    private readonly bool isGfx;
    private readonly bool hasViewChoice;
    private TargetFormat? target;

    /// <summary>Useful height of the options: always three rows, filled or not, so the layout does not move.</summary>
    public const int ContentHeight = 12 + 3 * 32;

    /// <summary>Raised when an option that changes the preview is modified.</summary>
    public event EventHandler? DisplayChanged;

    /// <param name="detectedKind">Type detected in the first file of this extension.</param>
    public ExtensionOptions(string extension, ViewKind detectedKind)
    {
        // The BT2 and GFX options follow the extension (the only source of these two types), even if a file is unreadable
        isBt2 = extension.Equals(".bt2", StringComparison.OrdinalIgnoreCase);
        isGfx = extension.Equals(".gfx", StringComparison.OrdinalIgnoreCase);
        // A text file or a palette can also be shown as hex; an image has only its image rendering
        hasViewChoice = detectedKind is ViewKind.Text or ViewKind.Palette;
        if (detectedKind == ViewKind.Palette)
        {
            cboView.Items[0] = new Choice<ViewMode>("Palette", ViewMode.Auto);
            cboView.SelectedIndex = 0;
        }

        lblGfxPalette.Text = isGfx ? "GFX palette:" : "Output GFX palette:";
        lblPalette.Text = isBt2 ? "Palette:" : "BT2 palette:";

        toolTip.SetToolTip(cboPalette, isBt2
            ? "Palette used to display the image and to convert its colors. Auto: look down for EXTRAPAN, preview for PREVIEW, standard otherwise."
            : "Palette the BT2 colors are matched against. Auto: look down for EXTRAPAN, preview for PREVIEW, standard otherwise.");
        toolTip.SetToolTip(cboColors,
            "Panel colors: only the indices used by the original cockpit art (0, 142-143, 160-255), which all have night variants.\n" +
            "Full palette: all 256 colors, but some of them change with fog, night or HUD brightness.");
        toolTip.SetToolTip(chkDither, "Floyd-Steinberg error diffusion: smoother gradients for photos or renders, grainier flat areas.");
        toolTip.SetToolTip(cboTarget, "Auto (SVGA mod): 800 x 600, PREVIEW 320 x 200.\nAuto (original game): original size of each file (PILOTPAN 320 x 104, PSIDEPAN 320 x 40...).");
        if (isBt2)
        {
            toolTip.SetToolTip(cboMismatch,
                "BT2 to BT2 resamples the pixels (nearest neighbor), so palette indices stay exact.\n" +
                "Stretch: fill the target size. Fit: keep the aspect ratio, bars use index 0. Skip: leave files of another size unconverted.");
        }
        toolTip.SetToolTip(chkBackup, "Before overwriting NAME.BT2, copy it to NAME.BT2.bak (an existing .bak is never overwritten).");
        toolTip.SetToolTip(chkTransparent, "Draw palette index 0 as transparent, in the preview and in the converted file.");
        toolTip.SetToolTip(cboGfxPalette, isGfx
            ? "GFX files contain no palette: AMP.EXE sets one of several 16-color sets depending on the screen.\n" +
              "Auto: Debrief for DBnn / DEBRIEF / DUALDEB, Menu (grayscale) for the others. Change it if the colors look wrong."
            : "The 16-color set the image is reduced to. Auto: Debrief for DBnn / DEBRIEF / DUALDEB, Menu (grayscale) for the others.");

        cboView.SelectedIndexChanged += (_, _) => DisplayChanged?.Invoke(this, EventArgs.Empty);
        if (isBt2)
        {
            cboPalette.SelectedIndexChanged += (_, _) => DisplayChanged?.Invoke(this, EventArgs.Empty);
            chkTransparent.CheckedChanged += (_, _) => DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
        if (isGfx)
            cboGfxPalette.SelectedIndexChanged += (_, _) => DisplayChanged?.Invoke(this, EventArgs.Empty);

        // Rows created once, each control in its final row: they are only shown or hidden.
        // A Win32 ComboBox sends its notifications to the parent known at its creation: moved afterwards to another
        // container, it would no longer raise SelectedIndexChanged.
        // Row 1: what is needed to display and decode the source file
        if (hasViewChoice)
            sourceRow.Controls.AddRange([lblView, cboView]);
        if (isBt2)
            sourceRow.Controls.AddRange([lblPalette, cboPalette, chkTransparent]);
        else if (isGfx)
            sourceRow.Controls.AddRange([lblGfxPalette, cboGfxPalette]);

        // Next rows: what is needed to encode to the destination format (no GFX to GFX).
        // BT2 to BT2 only changes the size: palette indices are kept, so no palette, colors or dithering.
        if (!isBt2)
            bt2ColorRow.Controls.AddRange([lblPalette, cboPalette, lblColors, cboColors, chkDither]);
        bt2SizeRow.Controls.AddRange([lblTargetSize, cboTarget, lblMismatch, cboMismatch, chkBackup]);
        if (!isGfx)
            gfxTargetRow.Controls.AddRange([lblGfxPalette, cboGfxPalette]);
        noneRow.Controls.Add(lblNone);

        rows.Controls.AddRange([sourceRow, bt2ColorRow, bt2SizeRow, gfxTargetRow, noneRow]);
        Controls.Add(rows);
        UpdateRows();
    }

    /// <summary>Chosen destination format: changes the conversion options shown (null: no conversion).</summary>
    public void SetTarget(TargetFormat? newTarget)
    {
        if (newTarget == target)
            return;
        target = newTarget;
        UpdateRows();
    }

    private static FlowLayoutPanel NewRow() => new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };

    private void UpdateRows()
    {
        rows.SuspendLayout();
        sourceRow.Visible = sourceRow.Controls.Count > 0;
        bt2SizeRow.Visible = target == TargetFormat.Bt2;
        bt2ColorRow.Visible = bt2SizeRow.Visible && !isBt2;
        gfxTargetRow.Visible = target == TargetFormat.Gfx && !isGfx;
        noneRow.Visible = !sourceRow.Visible && !bt2ColorRow.Visible && !gfxTargetRow.Visible;
        rows.ResumeLayout(true);
    }

    public ViewMode View => hasViewChoice ? Ui.Selected<ViewMode>(cboView) : ViewMode.Auto;

    /// <summary>Options passed to the decoder and the converter: each palette is only used on the side (source or destination) where it is shown.</summary>
    public ConversionOptions ConversionOptions
    {
        get
        {
            GfxPaletteKind? gfxPalette = Ui.Selected<GfxPaletteKind?>(cboGfxPalette);
            PaletteKind? palette = Ui.Selected<PaletteKind?>(cboPalette);
            var bt2Target = new Bt2OutputSettings(
                palette,
                Ui.Selected<ColorRange>(cboColors),
                chkDither.Checked,
                Ui.Selected<TargetSize>(cboTarget),
                Ui.Selected<SizeMismatch>(cboMismatch));
            return new ConversionOptions(
                isBt2 ? palette : null,
                isBt2 && chkTransparent.Checked,
                bt2Target,
                chkBackup.Checked,
                isGfx ? gfxPalette : null,
                isGfx ? null : gfxPalette);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            toolTip.Dispose();
        base.Dispose(disposing);
    }
}
