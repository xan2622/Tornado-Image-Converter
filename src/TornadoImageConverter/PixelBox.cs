using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace TornadoImageConverter;

/// <summary>Displays an image fitted to the area, without smoothing, on a checkerboard (for transparency).</summary>
internal sealed class PixelBox : Control
{
    private Image? image;

    public PixelBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(48, 48, 48);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image
    {
        get => image;
        set
        {
            image = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (image is null)
            return;

        Rectangle area = ClientRectangle;
        area.Inflate(-8, -8);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        float scale = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
        if (scale >= 1)
            scale = MathF.Floor(scale);  // integer zoom: sharp, regular pixels

        int width = Math.Max(1, (int)(image.Width * scale));
        int height = Math.Max(1, (int)(image.Height * scale));
        var dest = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);

        using (var checker = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(200, 200, 200), Color.White))
            e.Graphics.FillRectangle(checker, dest);

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(image, dest);
    }
}
