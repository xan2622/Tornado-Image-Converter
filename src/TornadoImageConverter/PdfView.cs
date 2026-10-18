using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TornadoImageConverter;

/// <summary>PDF viewer of the Explore tab (Windows.Data.Pdf API: one page at a time, with zoom).</summary>
internal sealed class PdfView : UserControl
{
    private const string FitWidth = "Fit width";

    private readonly Button btnPrevious = new() { Text = "<", Width = 36 };
    private readonly Button btnNext = new() { Text = ">", Width = 36 };
    private readonly NumericUpDown pageNumber = new() { Width = 64, Minimum = 1, Maximum = 1, Margin = new Padding(3, 4, 3, 3) };
    private readonly Label lblPageCount = new() { AutoSize = true, Margin = new Padding(3, 8, 12, 3), Text = "/ 0" };
    private readonly ComboBox cboZoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(3, 4, 3, 3) };
    private readonly Panel scroller = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(70, 70, 70) };
    private readonly PictureBox page = new() { SizeMode = PictureBoxSizeMode.AutoSize };
    private readonly Label lblMessage = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Visible = false };
    private readonly System.Windows.Forms.Timer resizeTimer = new() { Interval = 250 };

    private PdfDocument? document;
    private int renderVersion;
    private bool updatingPageNumber;

    public PdfView()
    {
        cboZoom.Items.AddRange([FitWidth, "50%", "75%", "100%", "125%", "150%", "200%"]);
        cboZoom.SelectedIndex = 0;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 0, 0) };
        toolbar.Controls.AddRange([btnPrevious, btnNext, Ui.Caption("Page"), pageNumber, lblPageCount, Ui.Caption("Zoom:"), cboZoom]);

        scroller.Controls.Add(page);
        scroller.Controls.Add(lblMessage);
        Controls.Add(scroller);
        Controls.Add(toolbar);

        btnPrevious.Click += (_, _) => GoTo((int)pageNumber.Value - 1);
        btnNext.Click += (_, _) => GoTo((int)pageNumber.Value + 1);
        pageNumber.ValueChanged += (_, _) =>
        {
            if (!updatingPageNumber)
                _ = ShowPageAsync();
        };
        cboZoom.SelectedIndexChanged += (_, _) => _ = ShowPageAsync();

        // In "Fit width" mode, the page is rendered again once resizing is finished
        scroller.SizeChanged += (_, _) =>
        {
            resizeTimer.Stop();
            resizeTimer.Start();
        };
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            if ((string?)cboZoom.SelectedItem == FitWidth)
                _ = ShowPageAsync();
        };
        scroller.MouseEnter += (_, _) => scroller.Focus();
    }

    /// <summary>Opens the document and shows its first page.</summary>
    public async Task OpenAsync(string path)
    {
        Close();
        int version = renderVersion;
        ShowMessage("Loading...");

        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        PdfDocument loaded = await PdfDocument.LoadFromFileAsync(file);
        if (version != renderVersion)
            return;

        document = loaded;
        updatingPageNumber = true;
        pageNumber.Maximum = Math.Max(1, (int)loaded.PageCount);
        pageNumber.Value = 1;
        updatingPageNumber = false;
        lblPageCount.Text = $"/ {loaded.PageCount}";
        await ShowPageAsync();
    }

    /// <summary>Drops the current document (selection change).</summary>
    public void Close()
    {
        renderVersion++;
        document = null;
        SetPageImage(null);
        lblPageCount.Text = "/ 0";
    }

    private void GoTo(int number)
    {
        if (number >= pageNumber.Minimum && number <= pageNumber.Maximum)
            pageNumber.Value = number;
    }

    private async Task ShowPageAsync()
    {
        if (document is null)
            return;

        int version = ++renderVersion;
        PdfDocument current = document;
        int index = (int)pageNumber.Value - 1;

        try
        {
            Bitmap bitmap = await RenderAsync(current, index);
            if (version != renderVersion)
            {
                bitmap.Dispose();
                return;
            }
            SetPageImage(bitmap);
            scroller.AutoScrollPosition = Point.Empty;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or ArgumentException)
        {
            if (version == renderVersion)
                ShowMessage("Cannot display this page: " + ex.Message);
        }
    }

    private async Task<Bitmap> RenderAsync(PdfDocument pdf, int index)
    {
        using PdfPage pdfPage = pdf.GetPage((uint)index);

        double scale = (string?)cboZoom.SelectedItem is { } zoom && zoom != FitWidth
            ? int.Parse(zoom.TrimEnd('%')) / 100.0
            : Math.Max(100, scroller.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8) / pdfPage.Size.Width;

        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(pdfPage.Size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(pdfPage.Size.Height * scale)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
        };

        using var stream = new InMemoryRandomAccessStream();
        await pdfPage.RenderToStreamAsync(stream, options);

        stream.Seek(0);
        using Stream input = stream.AsStreamForRead();
        using var copy = new MemoryStream();
        await input.CopyToAsync(copy);
        copy.Position = 0;
        using var decoded = new Bitmap(copy);
        return new Bitmap(decoded);  // copy independent of the stream
    }

    private void ShowMessage(string text)
    {
        SetPageImage(null);
        lblMessage.Text = text;
        lblMessage.Visible = true;
    }

    private void SetPageImage(Bitmap? bitmap)
    {
        Image? old = page.Image;
        page.Image = bitmap;
        old?.Dispose();
        page.Visible = bitmap is not null;
        if (bitmap is not null)
            lblMessage.Visible = false;
        CenterPage();
    }

    private void CenterPage()
    {
        int x = Math.Max(0, (scroller.ClientSize.Width - page.Width) / 2);
        page.Location = new Point(x, 8);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        CenterPage();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            renderVersion++;
            resizeTimer.Dispose();
            page.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
