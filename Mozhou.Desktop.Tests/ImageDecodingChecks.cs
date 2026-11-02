using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mozhou.Desktop;

internal static class ImageDecodingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "image-decoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var red = Color.FromRgb(220, 30, 40);
        var blue = Color.FromRgb(25, 65, 220);

        var jpeg = Path.Combine(root, "landscape.jpg");
        WriteImage(jpeg, new JpegBitmapEncoder { QualityLevel = 95 }, SolidImage(192, 96, red));
        CheckDecoded(jpeg, 64, 32, red, 6, "JPEG", check);

        var bmp = Path.Combine(root, "portrait.bmp");
        WriteImage(bmp, new BmpBitmapEncoder(), SolidImage(96, 192, blue));
        CheckDecoded(bmp, 32, 64, blue, 0, "BMP", check);

        var gif = Path.Combine(root, "two-frames.gif");
        WriteImage(gif, new GifBitmapEncoder(), SolidImage(160, 80, red), SolidImage(160, 80, blue));
        using (var stream = File.OpenRead(gif))
        {
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            check(decoder.Frames.Count == 2 && Matches(CenterColor(decoder.Frames[0]), red, 0)
                && Matches(CenterColor(decoder.Frames[1]), blue, 0),
                "GIF fixture contains two differently colored real encoded frames");
        }
        CheckDecoded(gif, 64, 32, red, 0, "GIF first frame", check);

        var damaged = Path.Combine(root, "signature-only.png");
        File.WriteAllBytes(damaged, [137, 80, 78, 71, 13, 10, 26, 10]);
        CheckRejected(damaged, error => true, "PNG signature without image data is rejected", check);

        var oversized = Path.Combine(root, "over-limit.png");
        using (var stream = File.Create(oversized)) stream.SetLength(25L * 1024 * 1024 + 1);
        CheckRejected(oversized, error => error.Message.Contains("25 MiB", StringComparison.Ordinal),
            "file above 25 MiB is rejected before image decoding", check);

        var tooManyPixels = Path.Combine(root, "over-pixel-limit.png");
        // This 120-byte fixture has a valid IHDR/CRC declaring 8000x5001 pixels, but only
        // one pixel of compressed data. Rejecting it by size proves decoding was not attempted.
        File.WriteAllBytes(tooManyPixels, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAH0AAABOJCAYAAAD3toj8AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXYzDSkPsPAAKAAXjLcuAzAAAAAElFTkSuQmCC"));
        using (var stream = File.OpenRead(tooManyPixels))
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            check(decoder.Frames[0].PixelWidth == 8000 && decoder.Frames[0].PixelHeight == 5001,
                "tiny PNG fixture exposes over 40 million pixels without allocating image data");
        }
        CheckRejected(tooManyPixels, error => error.Message.Contains("4000 万", StringComparison.Ordinal),
            "image above 40 million source pixels is rejected before pixel decoding", check);
    }

    private static void CheckDecoded(string path, int expectedWidth, int expectedHeight, Color expectedColor,
        int tolerance, string format, Action<bool, string> check)
    {
        LocalImageLoader.Validate(path);
        check(true, format + " passes real image validation");
        var thumbnail = LocalImageLoader.Load(path, 64);
        check(thumbnail.PixelWidth == expectedWidth && thumbnail.PixelHeight == expectedHeight,
            format + " thumbnail preserves aspect ratio with a 64 pixel long edge");
        check(thumbnail.IsFrozen, format + " decoded pixels are frozen");
        check(Matches(CenterColor(thumbnail), expectedColor, tolerance), format + " returns the expected decoded image pixels");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(true, format + " loading releases the source file handle");
    }

    private static void CheckRejected(string path, Func<InvalidDataException, bool> reason,
        string message, Action<bool, string> check)
    {
        InvalidDataException? rejection = null;
        try { LocalImageLoader.Validate(path); }
        catch (InvalidDataException error) { rejection = error; }
        check(rejection != null && reason(rejection), message);
    }

    private static BitmapSource SolidImage(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B; pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static void WriteImage(string path, BitmapEncoder encoder, params BitmapSource[] frames)
    {
        foreach (var frame in frames) encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Color CenterColor(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        int offset = (converted.PixelHeight / 2 * converted.PixelWidth + converted.PixelWidth / 2) * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static bool Matches(Color actual, Color expected, int tolerance) => actual.A == 255
        && Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance
        && Math.Abs(actual.B - expected.B) <= tolerance;

}
