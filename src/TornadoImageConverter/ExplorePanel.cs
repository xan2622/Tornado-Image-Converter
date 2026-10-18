using System.Runtime.InteropServices;

namespace TornadoImageConverter;

/// <summary>
/// "Explore" tab: lists the files of a game folder, displays them (image, text, hex, audio, PDF)
/// and converts the selection to another game image format.
/// </summary>
internal sealed class ExplorePanel : UserControl
{
    private enum Surface
    {
        Image,
        Text,
        Audio,
        Pdf
    }

    private sealed record Entry(string Path, long Size, ViewKind Kind, string Description)
    {
        public string Extension => System.IO.Path.GetExtension(Path);
    }

    private readonly Button btnSource = Ui.FolderButton("Source folder");
    private readonly Label lblSource = Ui.PathLabel();
    private readonly Button btnDest = Ui.FolderButton("Destination folder");
    private readonly Label lblDest = Ui.PathLabel();

    private readonly Button btnSelectAll = new() { Text = "Select all", AutoSize = true };
    private readonly Button btnSelectSameType = new() { Text = "Select all of same file type", AutoSize = true };
    private readonly Button btnInvert = new() { Text = "Invert selection", AutoSize = true };
    private readonly Button btnSelectNone = new() { Text = "Select none", AutoSize = true };
    private readonly Button btnConvert = new() { Text = "Convert", AutoSize = true, Enabled = false };
    private readonly Label lblTo = Ui.Caption("to:");
    private readonly ComboBox cboTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(3, 4, 3, 3) };

    private readonly TabControl typeTabs = new() { Dock = DockStyle.Fill };
    // Fixed height (three option rows): the list and the preview do not move with the selection
    private readonly Panel typePanel = new() { Dock = DockStyle.Top, Height = ExtensionOptions.ContentHeight + 40, Padding = new Padding(8, 0, 8, 4) };
    private readonly Dictionary<string, ExtensionOptions> optionsByExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabPage> pagesByExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListView lvFiles = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
    private readonly PixelBox preview = new() { Dock = DockStyle.Fill };
    private readonly TextBox viewer = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        HideSelection = false,
        BackColor = SystemColors.Window,
        Font = new Font("Consolas", 9.75f),
        Visible = false
    };
    private readonly AudioView audio = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly PdfView pdf = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label lblInfo = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), AutoEllipsis = true };
    private readonly ToolStripStatusLabel lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Click Source folder to open a folder." };
    private readonly ToolStripProgressBar progress = new() { Visible = false };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill };
    private bool updatingTargets;
    private readonly System.Windows.Forms.Timer selectionTimer = new() { Interval = 80 };

    private string? sourceDir;
    private string? destDir;
    private Bitmap? currentBitmap;
    private int refreshVersion;

    public bool IsBusy { get; private set; }

    public string? SourceDir => sourceDir;
    public string? DestDir => destDir;

    /// <summary>Raised when the source or destination folder changes (so the choice can be remembered).</summary>
    public event EventHandler? FoldersChanged;

    public ExplorePanel()
    {
        lvFiles.Columns.Add("File", 160);
        lvFiles.Columns.Add("Type", 300);
        lvFiles.Columns.Add("Size", 90, HorizontalAlignment.Right);
        Ui.EnableColumnSorting(lvFiles, SortKind.Text, SortKind.Text, SortKind.Number);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(btnSource, 0, 0);
        top.Controls.Add(lblSource, 1, 0);
        top.Controls.Add(btnDest, 0, 1);
        top.Controls.Add(lblDest, 1, 1);

        var separator = new Label { BorderStyle = BorderStyle.Fixed3D, Width = 2, Height = 24, AutoSize = false, Margin = new Padding(10, 3, 10, 3) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4), WrapContents = true };
        actions.Controls.AddRange([btnSelectAll, btnSelectSameType, btnInvert, btnSelectNone, separator, btnConvert, lblTo, cboTarget]);
        lblTo.Visible = cboTarget.Visible = false;

        typePanel.Controls.Add(typeTabs);

        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(viewer);
        host.Controls.Add(audio);
        host.Controls.Add(pdf);
        host.Controls.Add(preview);

        split.Panel1.Controls.Add(lvFiles);
        split.Panel2.Controls.Add(host);
        split.Panel2.Controls.Add(lblInfo);
        Ui.InitSplitter(split);

        var status = new StatusStrip();
        status.Items.AddRange([lblStatus, progress]);

        // Add order: the Fill control first so that Top and Bottom are laid out before it
        Controls.Add(split);
        Controls.Add(typePanel);
        Controls.Add(actions);
        Controls.Add(top);
        Controls.Add(status);

        btnSource.Click += (_, _) =>
        {
            if (Ui.PickFolder(this, "Select a folder containing game files", sourceDir) is { } selected)
                LoadFolder(selected);
        };
        btnDest.Click += (_, _) =>
        {
            if (Ui.PickFolder(this, "Select the destination folder for converted files", destDir ?? sourceDir) is not { } selected)
                return;
            destDir = selected;
            lblDest.Text = selected;
            UpdateConvertUi();
            FoldersChanged?.Invoke(this, EventArgs.Empty);
        };

        btnSelectAll.Click += (_, _) => SetSelection(_ => true);
        btnInvert.Click += (_, _) => SetSelection(item => !item.Selected);
        btnSelectNone.Click += (_, _) => SetSelection(_ => false);
        btnSelectSameType.Click += (_, _) => SelectSameType();
        btnConvert.Click += BtnConvert_Click;
        cboTarget.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingTargets)
                ApplyTarget();
        };

        // Several row state changes arrive in a row (Select all, Shift+click): a single refresh
        selectionTimer.Tick += (_, _) =>
        {
            selectionTimer.Stop();
            RefreshView();
            UpdateConvertUi();
        };
        lvFiles.SelectedIndexChanged += (_, _) =>
        {
            selectionTimer.Stop();
            selectionTimer.Start();
        };
    }

    /// <summary>Reloads the folders of the previous session, if they still exist.</summary>
    public void RestoreFolders(string? source, string? dest)
    {
        if (dest is not null && Directory.Exists(dest))
        {
            destDir = dest;
            lblDest.Text = dest;
        }
        if (source is not null && Directory.Exists(source))
            LoadFolder(source);
        UpdateConvertUi();
    }

    private void LoadFolder(string path)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        sourceDir = path;
        lblSource.Text = path;

        lvFiles.BeginUpdate();
        lvFiles.Items.Clear();
        foreach (string file in files)
        {
            Entry entry;
            try
            {
                (ViewKind kind, string description) = FileKinds.Classify(file);
                entry = new Entry(file, new FileInfo(file).Length, kind, description);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entry = new Entry(file, 0, ViewKind.Hex, ex.Message);
            }

            var item = new ListViewItem(Path.GetFileName(file)) { Tag = entry };
            item.SubItems.Add(entry.Description);
            item.SubItems.Add($"{entry.Size:N0} B");
            lvFiles.Items.Add(item);
        }
        lvFiles.EndUpdate();

        lblStatus.Text = $"{files.Length} file(s) in this folder.";
        if (lvFiles.Items.Count > 0)
            lvFiles.Items[0].Selected = true;
        else
        {
            RefreshView();
            UpdateConvertUi();
        }

        FoldersChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Selection ----

    private void SetSelection(Func<ListViewItem, bool> shouldSelect)
    {
        lvFiles.BeginUpdate();
        foreach (ListViewItem item in lvFiles.Items)
            item.Selected = shouldSelect(item);
        lvFiles.EndUpdate();
        lvFiles.Focus();
    }

    /// <summary>Selects all files with the same extension as the current file.</summary>
    private void SelectSameType()
    {
        ListViewItem? reference = CurrentItem();
        if (reference?.Tag is not Entry referenceEntry)
            return;

        SetSelection(item => item.Tag is Entry entry
            && entry.Extension.Equals(referenceEntry.Extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File shown in the preview: the focused file if it is selected, otherwise the first selected one.</summary>
    private ListViewItem? CurrentItem()
    {
        if (lvFiles.FocusedItem is { Selected: true } focused)
            return focused;
        return lvFiles.SelectedItems.Count > 0 ? lvFiles.SelectedItems[0] : lvFiles.FocusedItem;
    }

    private List<Entry> SelectedEntries() =>
        lvFiles.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Entry>().ToList();

    // ---- Conversion ----

    /// <summary>Destination formats common to all selected files; empty if one of them cannot be converted.</summary>
    private void UpdateConvertUi()
    {
        List<Entry> selected = SelectedEntries();
        UpdateTypeTabs(selected);

        IEnumerable<TargetFormat>? common = null;
        foreach (Entry entry in selected)
        {
            TargetFormat[] targets = ImageConversion.TargetsFor(entry.Kind, entry.Extension);
            common = common is null ? targets : common.Intersect(targets);
        }
        TargetFormat[] formats = common?.ToArray() ?? [];

        object? previous = cboTarget.SelectedItem;
        updatingTargets = true;
        cboTarget.Items.Clear();
        foreach (TargetFormat format in formats)
            cboTarget.Items.Add(new Choice<TargetFormat>(ImageConversion.Label(format), format));
        if (formats.Length > 0)
        {
            int index = previous is Choice<TargetFormat> p ? Array.IndexOf(formats, p.Value) : -1;
            cboTarget.SelectedIndex = Math.Max(0, index);
        }
        updatingTargets = false;
        ApplyTarget();

        lblTo.Visible = cboTarget.Visible = formats.Length > 0;
        btnConvert.Enabled = !IsBusy && formats.Length > 0 && destDir is not null;
    }

    private TargetFormat? SelectedTarget() => cboTarget.SelectedItem is Choice<TargetFormat> choice ? choice.Value : null;

    /// <summary>The conversion options shown follow the chosen destination format.</summary>
    private void ApplyTarget()
    {
        TargetFormat? target = SelectedTarget();
        foreach (ExtensionOptions options in optionsByExtension.Values)
            options.SetTarget(target);
    }

    /// <summary>
    /// A modified option applies to the file of its tab: if the preview shows a file of another extension
    /// (mixed selection), it switches to the first selected file of that extension.
    /// </summary>
    private void ShowFirstOfExtension(string extension)
    {
        if ((CurrentItem()?.Tag as Entry)?.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase) == true)
            return;

        ListViewItem? item = lvFiles.SelectedItems.Cast<ListViewItem>()
            .FirstOrDefault(i => i.Tag is Entry e && e.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;
        item.Focused = true;
        item.EnsureVisible();
    }

    /// <summary>Options of an extension (created on first use, then kept with their values).</summary>
    private ExtensionOptions OptionsFor(Entry entry)
    {
        if (!optionsByExtension.TryGetValue(entry.Extension, out ExtensionOptions? options))
        {
            options = new ExtensionOptions(entry.Extension, entry.Kind) { Dock = DockStyle.Fill };
            options.SetTarget(SelectedTarget());
            string extension = entry.Extension;
            options.DisplayChanged += (_, _) =>
            {
                if (IsBusy)
                    return;
                ShowFirstOfExtension(extension);
                RefreshView();
            };
            optionsByExtension[entry.Extension] = options;
        }
        return options;
    }

    /// <summary>One options tab per extension present in the selection (PNG, FNT, GFX...).</summary>
    private void UpdateTypeTabs(List<Entry> selected)
    {
        string[] extensions = selected.Select(e => e.Extension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? previousTab = typeTabs.SelectedTab?.Tag as string;
        string? currentExtension = (CurrentItem()?.Tag as Entry)?.Extension;

        bool unchanged = typeTabs.TabPages.Count == extensions.Length
            && typeTabs.TabPages.Cast<TabPage>().Zip(extensions).All(p => string.Equals((string?)p.First.Tag, p.Second, StringComparison.OrdinalIgnoreCase));
        if (!unchanged)
        {
            typeTabs.TabPages.Clear();
            foreach (string extension in extensions)
            {
                if (!pagesByExtension.TryGetValue(extension, out TabPage? page))
                {
                    ExtensionOptions options = OptionsFor(selected.First(e => e.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)));
                    page = new TabPage(extension.Length == 0 ? "(none)" : extension.TrimStart('.').ToUpperInvariant())
                    {
                        Tag = extension,
                        UseVisualStyleBackColor = true
                    };
                    page.Controls.Add(options);
                    pagesByExtension[extension] = page;
                }
                typeTabs.TabPages.Add(page);
            }

            // Keeps the current tab if it still exists, otherwise takes the one of the displayed file
            TabPage? wanted = typeTabs.TabPages.Cast<TabPage>().FirstOrDefault(p => string.Equals((string?)p.Tag, previousTab, StringComparison.OrdinalIgnoreCase))
                ?? typeTabs.TabPages.Cast<TabPage>().FirstOrDefault(p => string.Equals((string?)p.Tag, currentExtension, StringComparison.OrdinalIgnoreCase));
            if (wanted is not null)
                typeTabs.SelectedTab = wanted;
        }
    }

    private async void BtnConvert_Click(object? sender, EventArgs e)
    {
        if (destDir is null || cboTarget.SelectedItem is not Choice<TargetFormat> choice)
            return;

        List<Entry> entries = SelectedEntries();
        TargetFormat target = choice.Value;
        string OutputPath(Entry entry) => Path.Combine(destDir, ImageConversion.OutputFileName(entry.Path, target));

        int existing = entries.Count(entry => File.Exists(OutputPath(entry)));
        bool backup = target == TargetFormat.Bt2 && entries.Any(entry => OptionsFor(entry).ConversionOptions.BackupBt2);
        string extra = backup ? " A .bak copy of each existing BT2 will be kept." : "";
        if (!Ui.ConfirmOverwrite(this, existing, extra))
            return;

        SetBusy(true);
        progress.Maximum = entries.Count;
        progress.Value = 0;
        int succeeded = 0;
        var failures = new List<string>();

        foreach (Entry entry in entries)
        {
            lblStatus.Text = $"Converting {Path.GetFileName(entry.Path)}...";
            try
            {
                ConversionOptions options = OptionsFor(entry).ConversionOptions;
                await Task.Run(() => ImageConversion.Convert(entry.Path, entry.Kind, target, OutputPath(entry), options));
                succeeded++;
            }
            catch (Exception ex) when (IsLoadError(ex))
            {
                failures.Add($"{Path.GetFileName(entry.Path)}: {ex.Message}");
            }
            progress.Value++;
        }

        SetBusy(false);
        lblStatus.Text = failures.Count == 0
            ? $"{succeeded} file(s) converted to {destDir}"
            : $"{succeeded} file(s) converted, {failures.Count} failed.";
        if (failures.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, failures.Take(10)) + (failures.Count > 10 ? Environment.NewLine + "..." : ""),
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        foreach (Control control in new Control[] { btnSource, btnDest, btnSelectAll, btnSelectSameType, btnInvert, btnSelectNone, cboTarget, typeTabs, lvFiles })
            control.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
        UpdateConvertUi();
    }

    // ---- Preview ----

    private void RefreshView()
    {
        int version = ++refreshVersion;
        audio.Close();
        pdf.Close();
        SetBitmap(null);
        viewer.Clear();
        lblInfo.Text = "";

        if (CurrentItem()?.Tag is not Entry entry)
        {
            ShowSurface(Surface.Image);
            return;
        }

        ExtensionOptions options = OptionsFor(entry);
        ViewKind kind = options.View switch
        {
            ViewMode.Hex => ViewKind.Hex,
            _ => entry.Kind
        };
        string name = Path.GetFileName(entry.Path);
        int selectedCount = lvFiles.SelectedItems.Count;
        string prefix = selectedCount > 1 ? $"{selectedCount} selected   |   " : "";

        try
        {
            switch (kind)
            {
                case ViewKind.Bt2 or ViewKind.Gfx or ViewKind.Pt2 or ViewKind.Fnt or ViewKind.Image or ViewKind.Iff:
                    // Same decoding as the conversion: the preview shows exactly what will be converted
                    ConversionOptions decoding = options.ConversionOptions;
                    SourceImage image = ImageConversion.Decode(entry.Path, kind, decoding);
                    ShowBitmap(Bitmaps.FromArgb(image.Width, image.Height, image.Argb),
                        $"{prefix}{name}   |   {image.Width} x {image.Height}   |   {ImageDetails(entry, decoding)}");
                    break;

                case ViewKind.Palette:
                    (int firstIndex, int[] colors) = RgbPalette.Load(entry.Path);
                    ShowBitmap(RgbPalette.Render(firstIndex, colors),
                        $"{prefix}{name}   |   {entry.Description}   |   indices {firstIndex} to {firstIndex + colors.Length - 1}");
                    break;

                case ViewKind.Audio:
                    audio.Open(entry.Path);
                    ShowSurface(Surface.Audio);
                    lblInfo.Text = $"{prefix}{name}   |   {entry.Size / 1024.0 / 1024.0:0.0} MB   |   {entry.Description}";
                    break;

                case ViewKind.Pdf:
                    ShowSurface(Surface.Pdf);
                    lblInfo.Text = $"{prefix}{name}   |   {entry.Size / 1024.0 / 1024.0:0.0} MB   |   {entry.Description}";
                    _ = ShowPdfAsync(entry, version);
                    break;

                case ViewKind.Text:
                    ShowText(FileKinds.ReadText(entry.Path, out bool textTruncated), $"{prefix}{name}   |   {entry.Description}", textTruncated);
                    break;

                default:
                    string hexInfo = entry.Kind == ViewKind.Hex ? entry.Description : $"{entry.Description}   |   Hex dump";
                    ShowText(FileKinds.HexDump(entry.Path, out bool hexTruncated), $"{prefix}{name}   |   {hexInfo}", hexTruncated);
                    break;
            }
        }
        catch (Exception ex) when (IsLoadError(ex))
        {
            audio.Close();
            ShowText(ex.Message, name, truncated: false);
        }
    }

    /// <summary>Detail shown under an image preview: palette used for the formats that do not contain one.</summary>
    private static string ImageDetails(Entry entry, ConversionOptions options) => entry.Kind switch
    {
        ViewKind.Bt2 => $"Palette: {options.Bt2PaletteFor(entry.Path).DisplayName()}",
        ViewKind.Gfx => $"GFX, palette: {GfxPalettes.DisplayName(options.GfxPaletteFor(entry.Path))}",
        ViewKind.Pt2 => "PT2, embedded palette",
        _ => entry.Description
    };

    private async Task ShowPdfAsync(Entry entry, int version)
    {
        try
        {
            await pdf.OpenAsync(entry.Path);
        }
        catch (Exception ex)  // WinRT loading (protected or corrupt PDF...) throws a variety of exception types
        {
            if (version == refreshVersion)
            {
                pdf.Close();
                ShowText(ex.Message, Path.GetFileName(entry.Path), truncated: false);
            }
        }
    }

    private static bool IsLoadError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException or COMException;

    private void ShowBitmap(Bitmap bitmap, string info)
    {
        ShowSurface(Surface.Image);
        SetBitmap(bitmap);
        lblInfo.Text = info;
    }

    private void ShowText(string text, string info, bool truncated)
    {
        ShowSurface(Surface.Text);
        viewer.Text = text;
        lblInfo.Text = truncated ? info + "   |   (start of file only)" : info;
    }

    private void ShowSurface(Surface surface)
    {
        preview.Visible = surface == Surface.Image;
        viewer.Visible = surface == Surface.Text;
        audio.Visible = surface == Surface.Audio;
        pdf.Visible = surface == Surface.Pdf;
    }

    private void SetBitmap(Bitmap? bitmap)
    {
        preview.Image = bitmap;
        currentBitmap?.Dispose();
        currentBitmap = bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            selectionTimer.Dispose();
            currentBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
