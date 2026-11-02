using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mozhou.Desktop;

public static class LocalImageLoader
{
    public const long MaximumFileBytes = 25L * 1024 * 1024;
    public const long MaximumSourcePixels = 40_000_000;
    public const int ThumbnailPixels = 360;
    public const int PreviewPixels = 2000;

    public static void Validate(string path) => _ = Load(path, ThumbnailPixels);

    public static BitmapSource Load(string path, int maxPixels = ThumbnailPixels)
    {
        if (maxPixels is < 1 or > PreviewPixels)
            throw new ArgumentOutOfRangeException(nameof(maxPixels), $"图片解码尺寸须在 1 到 {PreviewPixels} 像素之间。");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > MaximumFileBytes)
            throw new InvalidDataException("图片不能为空，且不能超过 25 MiB。");

        try
        {
            // Inspect metadata before requesting pixel data. OnLoad here would load every GIF frame.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder is not (PngBitmapDecoder or JpegBitmapDecoder or BmpBitmapDecoder or GifBitmapDecoder))
                throw new InvalidDataException("仅支持 PNG、JPEG、BMP 和 GIF 图片。");
            if (decoder.Frames.Count == 0) throw new InvalidDataException("图片没有可读取的画面。");
            var frame = decoder.Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            if (width <= 0 || height <= 0 || (long)width * height > MaximumSourcePixels)
                throw new InvalidDataException("图片像素过大，单张图片最多支持 4000 万像素。");

            double scale = Math.Min(1.0, (double)maxPixels / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)Math.Floor(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Floor(height * scale));
            stream.Position = 0;
            Exception? decodeFailure = null;
            var bitmap = new BitmapImage();
            bitmap.DecodeFailed += (_, e) => decodeFailure = e.ErrorException;
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = targetWidth;
            bitmap.DecodePixelHeight = targetHeight;
            bitmap.EndInit();
            if (decodeFailure != null) throw new InvalidDataException("图片内容损坏，无法解码。", decodeFailure);
            if (bitmap.PixelWidth != targetWidth || bitmap.PixelHeight != targetHeight)
                throw new InvalidDataException("图片解码后的尺寸不正确。");

            // Copy all decoded pixels: this forces deferred codec errors and detaches the result
            // from the source stream. BMP/GIF can still decode at full source size internally.
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int stride = checked(targetWidth * 4);
            var pixels = new byte[checked(stride * targetHeight)];
            converted.CopyPixels(pixels, stride, 0);
            if (decodeFailure != null) throw new InvalidDataException("图片内容损坏，无法解码。", decodeFailure);
            var result = BitmapSource.Create(targetWidth, targetHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception error) when (error is NotSupportedException or FormatException or COMException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("图片格式不受支持，或图片内容已损坏。", error);
        }
    }
}
