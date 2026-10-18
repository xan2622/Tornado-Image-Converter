namespace TornadoImageConverter;

internal static class AppInfo
{
    public const string Name = "Tornado Image Converter";
    public const string Version = "1.0";
    public const string Author = "xan2622";
}

/// <summary>Drop-down list item: displayed label and associated value.</summary>
internal sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

internal static class Ui
{
    public static Choice<PaletteKind?>[] PaletteChoices() =>
    [
        new("Auto (by file name)", null),
        new("Standard panel", PaletteKind.Standard),
        new("Look down (EXTRAPAN)", PaletteKind.LookDown),
        new("Preview (PREVIEW)", PaletteKind.Preview)
    ];

    public static T Selected<T>(ComboBox comboBox) => ((Choice<T>)comboBox.SelectedItem!).Value;

    public static ComboBox DropDown<T>(Choice<T>[] choices, int width)
    {
        var comboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3, 4, 12, 3) };
        comboBox.Items.AddRange(choices);
        comboBox.SelectedIndex = 0;
        return comboBox;
    }

    public static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

    public static Label PathLabel() => new()
    {
        Text = "(no folder selected)",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    public static Button FolderButton(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

    public static string? PickFolder(IWin32Window owner, string description, string? initialFolder)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = initialFolder ?? ""
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>
    /// Sorts when a column header is clicked (reversed on the second click), with an arrow in the header.
    /// Sorting is one-off: the order of rows added afterwards is not changed.
    /// </summary>
    public static void EnableColumnSorting(ListView listView, params SortKind[] kinds)
    {
        int sortedColumn = -1;
        bool ascending = true;
        foreach (ColumnHeader header in listView.Columns)
            header.Tag = header.Text;

        listView.ColumnClick += (_, e) =>
        {
            if (e.Column >= kinds.Length)
                return;

            ascending = e.Column != sortedColumn || !ascending;
            sortedColumn = e.Column;

            listView.ListViewItemSorter = new ColumnSorter(e.Column, kinds[e.Column], ascending);
            listView.Sort();
            listView.ListViewItemSorter = null;

            foreach (ColumnHeader header in listView.Columns)
                header.Text = (string)header.Tag! + (header.Index == sortedColumn ? (ascending ? "  ▲" : "  ▼") : "");
        };
    }

    /// <summary>Splits the list / preview area at 55% as soon as the width is known.</summary>
    public static void InitSplitter(SplitContainer split)
    {
        void OnSizeChanged(object? sender, EventArgs e)
        {
            if (split.Width < 400)
                return;
            split.SplitterDistance = split.Width * 55 / 100;
            split.SizeChanged -= OnSizeChanged;
        }
        split.SizeChanged += OnSizeChanged;
    }

    public static bool ConfirmOverwrite(IWin32Window owner, int existing, string extra = "") =>
        existing == 0 || MessageBox.Show(owner,
            $"{existing} file(s) already exist in the destination folder and will be overwritten.{extra} Continue?",
            AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
}
