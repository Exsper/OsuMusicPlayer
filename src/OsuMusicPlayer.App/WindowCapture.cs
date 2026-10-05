namespace OsuMusicPlayer.App;

using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>窗口截图（用于 <c>--screenshot</c> 界面验证）。</summary>
internal static class WindowCapture
{
    private const uint PwRenderFullContent = 2;

    public static void Capture(Form form, string path)
    {
        using Bitmap bitmap = CaptureWindow(form);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static Bitmap CaptureWindow(Form form)
    {
        int width = Math.Max(1, form.Width);
        int height = Math.Max(1, form.Height);

        Bitmap bitmap = new(width, height);

        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);

            IntPtr hdc = graphics.GetHdc();
            bool captured;

            try
            {
                captured = PrintWindow(form.Handle, hdc, PwRenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!captured)
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
            }
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
}
