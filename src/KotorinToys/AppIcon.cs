using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace KotorinToys;

internal static class AppIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(32, 103, 205));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2);
        graphics.DrawRectangle(pen, 12, 7, 8, 5);
        graphics.DrawRectangle(pen, 7, 12, 18, 12);
        graphics.DrawLine(pen, 7, 17, 25, 17);
        graphics.DrawLine(pen, 16, 15, 16, 20);
        nint handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
