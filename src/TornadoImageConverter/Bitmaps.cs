using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TornadoImageConverter;

/// <summary>Conversions between pixel arrays and 32-bit ARGB GDI+ <see cref="Bitmap"/>.</summary>
public static class Bitmaps
{
    public static Bitmap FromArgb(int width, int height, int[] argb)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(argb, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    public static int[] ToArgb(Bitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var argb = new int[width * height];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * width, width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return argb;
    }
}
