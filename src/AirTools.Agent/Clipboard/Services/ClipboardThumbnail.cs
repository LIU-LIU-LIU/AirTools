using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AirTools.Tools.Clipboard.Services;

public static class ClipboardThumbnail
{
    public const int MaximumBytes = 393216;

    // Preserve transparency. Reduce dimensions rather than dropping detailed PNGs.
    public static byte[] Encode(BitmapSource source)
    {
        BitmapSource image = source;
        while (true)
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
            if (stream.Length <= MaximumBytes) return stream.ToArray();
            var width = Math.Max(1, (int)(image.PixelWidth * 0.85));
            var height = Math.Max(1, (int)(image.PixelHeight * 0.85));
            image = new TransformedBitmap(image, new ScaleTransform(
                (double)width / image.PixelWidth, (double)height / image.PixelHeight));
            image.Freeze();
        }
    }
}


