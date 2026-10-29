using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static void Main(string[] args)
    {
        try { Generate(args); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    }

    private static void Generate(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: GenerateIcon <Assets directory> [preview PNG path]");
        var assets = Path.GetFullPath(args[0]);
        var svg = XDocument.Load(Path.Combine(assets, "Mozhou.svg"));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var paths = svg.Root!.Elements(ns + "path").Select(element => (
            Geometry: Geometry.Parse((string)element.Attribute("d")!),
            Brush: (Brush)new BrushConverter().ConvertFromInvariantString((string)element.Attribute("fill")!)!
        )).ToArray();
        var frames = Sizes.Select(size => Render(size, context =>
        {
            context.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            foreach (var path in paths) context.DrawGeometry(path.Brush, null, path.Geometry);
            context.Pop();
        })).ToArray();

        // DIB frames keep small icons compatible with native Windows consumers; 256px uses PNG.
        var payloads = frames.Select((frame, index) => Sizes[index] == 256 ? Png(frame) : Dib(frame)).ToArray();
        using (var writer = new BinaryWriter(File.Create(Path.Combine(assets, "Mozhou.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)Sizes.Length);
            var offset = 6 + 16 * Sizes.Length;
            for (var i = 0; i < Sizes.Length; i++)
            {
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(payloads[i].Length); writer.Write(offset); offset += payloads[i].Length;
            }
            foreach (var payload in payloads) writer.Write(payload);
        }
        File.WriteAllBytes(Path.Combine(assets, "Mozhou.png"), Png(frames[^1]));
        using (var input = File.OpenRead(Path.Combine(assets, "Mozhou.ico")))
        {
            var decoded = new IconBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var decodedSizes = decoded.Frames.Select(frame => frame.PixelWidth).ToArray();
            // WIC may reorder frames by pixel format and size rather than ICO directory order.
            if (!decodedSizes.Order().SequenceEqual(Sizes) || decoded.Frames.Any(frame => frame.PixelWidth != frame.PixelHeight))
                throw new InvalidDataException("ICO decoded dimensions: " + string.Join(", ", decodedSizes));
        }
        if (args.Length == 2)
        {
            var preview = Render(1000, context => DrawPreview(context, frames));
            var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, Png(preview));
        }
        Console.WriteLine($"Created and decoded ICO frames: {string.Join(", ", Sizes)}. SVG contains no font dependency.");
    }

    private static RenderTargetBitmap Render(int size, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) draw(context);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }

    private static byte[] Dib(BitmapSource source)
    {
        var size = source.PixelWidth;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4]; converted.CopyPixels(pixels, size * 4, 0);
        var maskStride = ((size + 31) / 32) * 4;
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(40); writer.Write(size); writer.Write(size * 2);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
        writer.Write(pixels.Length + maskStride * size);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (var y = size - 1; y >= 0; y--) writer.Write(pixels, y * size * 4, size * 4);
        for (var y = size - 1; y >= 0; y--)
        {
            var mask = new byte[maskStride];
            for (var x = 0; x < size; x++) if (pixels[(y * size + x) * 4 + 3] == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(mask);
        }
        writer.Flush(); return output.ToArray();
    }

    private static void DrawPreview(DrawingContext context, BitmapSource[] frames)
    {
        Brush Color(string value) => (Brush)new BrushConverter().ConvertFromInvariantString(value)!;
        void Text(string text, double x, double y, double size, string color, string font = "Microsoft YaHei UI") =>
            context.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(font), size, Color(color), 1), new Point(x, y));
        context.DrawRectangle(Color("#F3EFE6"), null, new Rect(0, 0, 1000, 1000));
        Text("MOZHOU  /  墨舟", 60, 48, 17, "#716A61");
        Text("把故事，落成一枚印。", 60, 93, 38, "#2C2824", "SimSun");
        context.DrawImage(frames[^1], new Rect(340, 190, 320, 320));
        Text("藏书印", 440, 535, 29, "#2C2824", "SimSun");
        Text("朱砂  #9A4A3B   ·   纸白  #FFFCF6", 320, 584, 17, "#716A61");
        context.DrawLine(new Pen(Color("#DCD5C8"), 1), new Point(60, 643), new Point(940, 643));
        Text("实际像素尺寸", 60, 676, 15, "#716A61");
        var indexes = new[] { 0, 1, 2, 3, 5, 6, 8 };
        for (var i = 0; i < indexes.Length; i++)
        {
            var frame = frames[indexes[i]]; var center = 107 + i * 128;
            context.DrawImage(frame, new Rect(center - frame.PixelWidth / 2.0, 776 - frame.PixelHeight / 2.0, frame.PixelWidth, frame.PixelHeight));
            Text(frame.PixelWidth + " px", center - 20, 860, 13, "#716A61");
        }
        Text("原创矢量轮廓 · 透明外缘 · EXE / 任务栏 / 窗口统一图标", 60, 937, 15, "#716A61");
    }
}
