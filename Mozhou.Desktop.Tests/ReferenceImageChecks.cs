using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class ReferenceImageChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "portrait.png");
        var second = Path.Combine(root, "scene.png");
        WriteImage(first, 120, 80); WriteImage(second, 80, 120);
        var folder = Path.Combine(root, "novel");
        var store = new ProjectStore(folder);
        store.Save(new NovelProject { Title = "Image checks", Entries = [new NovelEntry { Kind = "character", Title = "Person" }, new NovelEntry { Kind = "world", Title = "Place" }] });
        var window = new MainWindow(null);
        try
        {
            Invoke(window, "OpenFolder", folder);
            var project = (NovelProject)Field(window, "_project")!;
            Invoke(window, "SelectEntry", project.Entries[0]);
            check((bool)Invoke(window, "AddReferenceImages", (object)new[] { first, second })!, "importing two reference images saves the selected entry");
            using var saved = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            var images = saved.RootElement.GetProperty("Entries")[0].GetProperty("Images");
            check(images.GetArrayLength() == 2, "multiple image associations survive saving");
            check(saved.RootElement.GetProperty("FormatVersion").GetInt32() == 2, "image-bearing manuscript prevents old editors dropping images");
            var managed = images.EnumerateArray().Select(x => Path.Combine(folder, "assets", "images", x.GetProperty("FileName").GetString()!)).ToArray();
            check(managed.All(File.Exists), "import copies both reference images into the novel");
            check(File.Exists(first) && File.Exists(second), "import leaves original pictures untouched");
            File.Delete(first); File.Delete(second);
            Invoke(window, "SelectEntry", project.Entries[1]);
            check(GalleryCount(window) == 0, "another entry does not inherit the previous entry pictures");
            Invoke(window, "SelectEntry", project.Entries[0]);
            check(GalleryCount(window) == 2, "reference pictures remain visible after external originals are deleted");
            var image = project.Entries[0].Images[0];
            check((bool)Invoke(window, "RemoveReferenceImage", image)!, "removing a picture saves its association change");
            check(GalleryCount(window) == 1 && managed.All(File.Exists), "removal keeps managed images for history restoration");
            var restored = new ProjectStore(folder).RestorePrevious();
            check(restored.Entries[0].Images.Count == 2,
                "restoring previous save restores removed picture association");
            Invoke(window, "OpenFolder", folder);
            project = (NovelProject)Field(window, "_project")!;
            Invoke(window, "SelectEntry", project.Entries[0]);
            check(GalleryCount(window) == 2, "reopening the novel reloads its saved reference gallery");
            ((TextBox)window.FindName("ContentBox")).Text = "unsaved image notes";
            var copyFolder = Path.Combine(root, "copy");
            Invoke(window, "SaveProjectCopy", copyFolder);
            var copy = new ProjectStore(copyFolder).Load();
            check(copy.Entries[0].Content == "unsaved image notes" && copy.Entries[0].Images.Count == 2,
                "save copy includes unsaved notes and reference associations");
            var copiedPath = new ProjectImageStore(copyFolder).ResolvePath(copy.Entries[0].Images[0]);
            check(File.ReadAllBytes(copiedPath).SequenceEqual(File.ReadAllBytes(managed[0])), "save copy preserves independent image bytes");
            check((bool)Invoke(window, "Flush")!, "image notes remain saveable after copying");
            File.Delete(managed[0]);
            Invoke(window, "SelectEntry", project.Entries[0]);
            check(GalleryCount(window) == 2, "missing image keeps a removable placeholder without blocking the entry");
            var before = File.ReadAllText(store.FilePath);
            try { Invoke(window, "SaveProjectCopy", Path.Combine(root, "missing-copy")); throw new Exception("Expected missing reference rejection"); }
            catch (TargetInvocationException error) when (error.InnerException is IOException) { }
            check(!File.Exists(Path.Combine(root, "missing-copy", "project.mozhou.json")), "missing image cannot produce an apparently complete novel copy");
            var invalid = Path.Combine(root, "broken.png"); File.WriteAllText(invalid, "not an image");
            try { Invoke(window, "AddReferenceImages", (object)new[] { managed[1], invalid }); throw new Exception("Expected invalid image rejection"); }
            catch (TargetInvocationException error) when (error.InnerException is IOException or InvalidDataException or NotSupportedException or ArgumentException or FormatException) { }
            check(File.ReadAllText(store.FilePath) == before && GalleryCount(window) == 2, "invalid image import preserves the manuscript and gallery");

            WriteImage(first, 120, 80);
            ((TextBox)window.FindName("ContentBox")).Text = "unsaved content";
            File.WriteAllText(store.FilePath, before.Replace("Image checks", "External edit"));
            var countBefore = Directory.GetFiles(Path.Combine(folder, "assets", "images")).Length;
            check(!(bool)Invoke(window, "AddReferenceImages", (object)new[] { first })!, "save conflict stops image import before copying assets");
            check(Directory.GetFiles(Path.Combine(folder, "assets", "images")).Length == countBefore, "blocked import leaves the managed image directory unchanged");
            // Restore the exact loaded baseline so disposing this test window cannot show a conflict dialog.
            File.WriteAllText(store.FilePath, before);
            check((bool)Invoke(window, "Flush")!, "image conflict fixture remains saveable after restoring the baseline");
            check((bool)Invoke(window, "TryShowBookshelf")! && GalleryCount(window) == 0, "returning to bookshelf releases the previous reference gallery");

            var thumbnail = LocalImageLoader.Load(first, 64);
            check(thumbnail.PixelWidth <= 64 && thumbnail.PixelHeight <= 64 && thumbnail.IsFrozen, "image loading bounds thumbnail size and freezes pixels");
            using (File.Open(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(true, "thumbnail decoding releases the source file handle");
        }
        finally
        {
            // A failed assertion must not leave the test runner waiting in a save-conflict dialog.
            typeof(MainWindow).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            window.Close();
        }
    }

    private static int GalleryCount(MainWindow window)
    {
        var view = (UserControl)window.FindName("ReferenceImages");
        return ((ItemsControl)view.FindName("ImageList")).Items.Count;
    }

    internal static void WriteImage(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4) { pixels[offset] = 80; pixels[offset + 1] = 130; pixels[offset + 2] = 180; pixels[offset + 3] = 255; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static object? Invoke(object target, string name, params object?[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
}
