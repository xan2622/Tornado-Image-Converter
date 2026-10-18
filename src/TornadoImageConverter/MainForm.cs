namespace TornadoImageConverter;

public sealed class MainForm : Form
{
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ExplorePanel explore = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = AppInfo.Name;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1250, 760);
        MinimumSize = new Size(900, 550);

        var explorePage = new TabPage("Explore") { UseVisualStyleBackColor = true };
        explorePage.Controls.Add(explore);
        var aboutPage = new TabPage("About") { UseVisualStyleBackColor = true };
        aboutPage.Controls.Add(new AboutPanel { Dock = DockStyle.Fill });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange([explorePage, aboutPage]);
        Controls.Add(tabs);

        // The tab reloads its remembered folders the first time it is shown
        void OnVisibleChanged(object? sender, EventArgs e)
        {
            if (!explore.Visible)
                return;
            explore.VisibleChanged -= OnVisibleChanged;
            BeginInvoke(() => explore.RestoreFolders(settings.ExploreDir, settings.ExploreDestDir));
        }
        explore.VisibleChanged += OnVisibleChanged;

        explore.FoldersChanged += (_, _) =>
        {
            settings.ExploreDir = explore.SourceDir;
            settings.ExploreDestDir = explore.DestDir;
            settings.Save();
        };

        FormClosing += (_, e) => e.Cancel = explore.IsBusy;
    }
}

/// <summary>"About" tab: software name, version, author and links.</summary>
internal sealed class AboutPanel : UserControl
{
    private const string RepoUrl = "https://github.com/xan2622/Tornado-Image-Converter";

    public AboutPanel()
    {
        var title = new Label { Text = AppInfo.Name, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var version = new Label { Text = $"Version {AppInfo.Version}", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        var author = new Label { Text = $"Author: {AppInfo.Author}", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        var repo = Link("GitHub: ", RepoUrl);

        var separator = new Label
        {
            AutoSize = false,
            Width = 620,
            Height = 2,
            BorderStyle = BorderStyle.Fixed3D,
            Margin = new Padding(0, 16, 0, 16)
        };

        var discord = Link("Frankie Kam's Tornado Discord server: ", "https://discord.com/invite/MKzBAxe");

        var kamTornado = Link("Frankie Kam's Tornado: ", "https://github.com/TornadoGame/Tornado");
        kamTornado.Margin = new Padding(0, 16, 0, 4);
        var kamTools = Link("Frankie Kam's Tools, Add-Ons and Patches: ", "https://github.com/TornadoGame/TornadoToolsAndPatches");

        var amalahamaSource = Link("Amalahama's DI-Tornado-Desert-Storm-1.0e-Source-Code: ", "https://github.com/amalahama/DI-Tornado-Desert-Storm-1.0e-Source-Code");
        amalahamaSource.Margin = new Padding(0, 16, 0, 4);
        var amalahamaSvga = Link("Amalahama's SVGA-Tornado-Release: ", "https://github.com/amalahama/SVGA-Tornado-Release");

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(40)
        };
        content.Controls.AddRange([title, version, author, repo, separator, discord,
            kamTornado, kamTools, amalahamaSource, amalahamaSvga]);
        Controls.Add(content);
    }

    /// <summary>Line of text where only the URL is clickable.</summary>
    private static LinkLabel Link(string prefix, string url)
    {
        var label = new LinkLabel
        {
            Text = prefix + url,
            AutoSize = true,
            LinkArea = new LinkArea(prefix.Length, url.Length),
            Margin = new Padding(0, 0, 0, 4)
        };
        label.LinkClicked += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        return label;
    }
}
