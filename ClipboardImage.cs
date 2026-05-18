using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GroqVoice;

/// <summary>
/// Puts a bitmap on the system clipboard under every format reasonable apps
/// look for: CF_DIB (image editors), PNG / image/png / PortableNetworkGraphics
/// (web + Electron apps), CF_HDROP (apps that accept pasted-as-a-file), and
/// CF_BITMAP (legacy fallback). 24bpp RGB throughout so the alpha channel
/// can't confuse anyone.
/// </summary>
public static class ClipboardImage
{
    public static void Set(Bitmap source)
    {
        // Flatten to 24bpp RGB. Some legacy CF_DIB consumers have no agreed
        // alpha convention and render ARGB pastes as black; killing alpha
        // dodges the whole class of bug.
        using var rgb = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(rgb))
            g.DrawImage(source, 0, 0, source.Width, source.Height);

        var data = new DataObject();

        // CF_DIB — preferred by Photoshop, Krita, GIMP, MS Office
        using (var bmpStream = new MemoryStream())
        {
            rgb.Save(bmpStream, ImageFormat.Bmp);
            var bmp = bmpStream.ToArray();
            var dib = new byte[bmp.Length - 14];                // strip 14-byte BITMAPFILEHEADER
            Buffer.BlockCopy(bmp, 14, dib, 0, dib.Length);
            data.SetData(DataFormats.Dib, false, dib);
        }

        // PNG bytes under multiple registered names — different apps use different keys
        byte[] pngBytes;
        using (var pngStream = new MemoryStream())
        {
            rgb.Save(pngStream, ImageFormat.Png);
            pngBytes = pngStream.ToArray();
        }
        data.SetData("PNG", false, pngBytes);
        data.SetData("image/png", false, pngBytes);
        data.SetData("PortableNetworkGraphics", false, pngBytes);

        // CF_HDROP — for apps that accept "paste a file"
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(),
                $"GroqVoice_snip_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            File.WriteAllBytes(tmp, pngBytes);
            data.SetData(DataFormats.FileDrop, true, new[] { tmp });
        }
        catch (Exception ex) { Log.Warn($"FileDrop attach failed: {ex.Message}"); }

        // CF_BITMAP last-resort fallback (autoconvert lets .NET hand it off to Win32)
        data.SetData(DataFormats.Bitmap, true, rgb);

        Clipboard.SetDataObject(data, copy: true);
    }
}
