using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Gwendoline;

/// <summary>
/// The tray icon: two emoji in one ink, black on a light taskbar and white on a dark one.
/// Segoe UI Emoji draws them at launch in its monochrome style, so no artwork ships with the app.
/// </summary>
internal sealed class EmojiIcon : IDisposable
{
    /// <summary>Gwendoline off, the PC sleeps normally: woman getting a massage.</summary>
    private const string OffEmoji = "\U0001F486‍♀️";
    /// <summary>Gwendoline on, the PC stays awake: woman technologist.</summary>
    private const string OnEmoji = "\U0001F469‍\U0001F4BB";

    /// <summary>Drawn this many times larger, then scaled down, for smooth edges without ClearType colors.</summary>
    private const int Oversample = 4;

    public Icon Off { get; private set; } = null!;
    public Icon On { get; private set; } = null!;

    public EmojiIcon()
    {
        Render();
    }

    /// <summary>Draws both icons again, for the current taskbar color and screen scale.</summary>
    public void Render()
    {
        var white = TaskbarIsDark();
        var side = SystemInformation.SmallIconSize.Width;
        var (off, on) = (Off, On);
        Off = Make(OffEmoji, side, white);
        On = Make(OnEmoji, side, white);
        Free(off);
        Free(on);
    }

    private static bool TaskbarIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
    }

    private static Icon Make(string emoji, int side, bool white)
    {
        var large = side * Oversample;
        using var drawing = new Bitmap(large, large, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(drawing))
        {
            graphics.Clear(Color.White);
            using var font = new Font("Segoe UI Emoji", large * 0.7f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, emoji, font, new Rectangle(0, 0, large, large), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        // Black on white: the darkness of each pixel becomes the ink's opacity.
        var ink = white ? 255 : 0;
        var data = drawing.LockBits(new Rectangle(0, 0, large, large), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * large];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var lightness = (0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]) / 255;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)ink;
            pixels[i + 3] = (byte)Math.Round(255 * (1 - lightness));
        }
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        drawing.UnlockBits(data);

        using var small = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(drawing, new Rectangle(0, 0, side, side));
        }
        var handle = small.GetHicon();
        // A copy the icon owns, so the handle can be freed right away.
        using var borrowed = Icon.FromHandle(handle);
        var icon = (Icon)borrowed.Clone();
        DestroyIcon(handle);
        return icon;
    }

    private static void Free(Icon? icon)
    {
        icon?.Dispose();
    }

    public void Dispose()
    {
        Free(Off);
        Free(On);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
