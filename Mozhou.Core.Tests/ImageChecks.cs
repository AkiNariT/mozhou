using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mozhou.Core;

internal static class ImageChecks
{
    // Tiny byte fixtures exercise Core's declared signature check; image decoding belongs to the desktop tests.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
    private static readonly byte[] Bmp = [0x42, 0x4D, 58, 0, 0, 0, 0, 0, 0, 0, 54, 0, 0, 0, 40, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 24, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xFF, 0xD9];

    internal static readonly (string Name, Action<string> Test)[] Cases =
    [
        ("Old novels without Images load with empty image lists", dir =>
        {
            var project = Project();
            var json = JsonSerializer.SerializeToNode(project)!.AsObject();
            foreach (var entry in json["Entries"]!.AsArray()) entry!.AsObject().Remove("Images");
            var path = new ProjectStore(dir).FilePath; File.WriteAllText(path, json.ToJsonString());
            var original = File.ReadAllBytes(path); var loaded = new ProjectStore(dir).Load();
            Equal(1, loaded.FormatVersion); True(loaded.Entries.All(entry => entry.Images.Count == 0), "old entries did not receive empty image lists");
            SameBytes(original, path, "loading migrated the manuscript on disk");
        }),
        ("First image promotes format 1 and removing images never downgrades format 2", dir =>
        {
            var store = new ProjectStore(dir); var project = Project(); store.Save(project);
            var images = new ProjectImageStore(dir); var image = images.Import(Source(dir, "portrait.png", Png));
            project.Entries[0].Images.Add(image); store.Save(project);
            Equal(2, project.FormatVersion); Equal(2, new ProjectStore(dir).Load().FormatVersion);
            var previous = JsonSerializer.Deserialize<NovelProject>(File.ReadAllText(store.FilePath + ".bak"))!;
            Equal(1, previous.FormatVersion); Equal(0, previous.Entries[0].Images.Count);
            project.Entries[0].Images.Clear(); store.Save(project);
            Equal(2, project.FormatVersion); Equal(2, new ProjectStore(dir).Load().FormatVersion);
            var restored = store.RestorePrevious(); Equal(2, restored.FormatVersion);
            Equal(image.FileName, restored.Entries[0].Images.Single().FileName);
            SameBytes(Png, images.ResolvePath(restored.Entries[0].Images.Single()), "history image bytes were lost after removal");
        }),
        ("Each look field alone promotes old manuscripts to format 3 and survives clearing and restoring", dir =>
        {
            foreach (var field in new[] { "Hairstyle", "Outfit", "LookName" })
            {
                var folder = Path.Combine(dir, field);
                using var store = new ProjectStore(folder);
                var image = store.Images.Import(Source(dir, field + ".png", Png));
                var project = Project(); project.Entries[0].Images.Add(image);
                if (field == "Outfit") store.Save(project); // exercise both v1 and v2 promotion
                var descriptor = JsonSerializer.SerializeToNode(image)!; descriptor[field] = "冬至";
                project.Entries[0].Images[0] = descriptor.Deserialize<EntryImage>()!;
                store.Save(project);
                Equal(3, store.Load().FormatVersion);
                var saved = JsonNode.Parse(File.ReadAllBytes(store.FilePath))!;
                Equal("冬至", (string?)saved["Entries"]![0]!["Images"]![0]![field]);
                project.Entries[0].Images[0] = image; store.Save(project);
                Equal(3, store.Load().FormatVersion);
                var restored = store.RestorePrevious();
                var restoredImage = restored.Entries[0].Images.Single();
                Equal("冬至", (string?)JsonSerializer.SerializeToNode(restoredImage)![field]);
                SameBytes(Png, store.Images.ResolvePath(restoredImage), "classification changed the image bytes");
            }
        }),
        ("Version 2 accepts missing look fields and rejects populated or null look fields without rewriting", dir =>
        {
            using var store = new ProjectStore(dir);
            var project = Project(); project.FormatVersion = 2;
            project.Entries[0].Images.Add(store.Images.Import(Source(dir, "legacy.png", Png)));
            var json = JsonSerializer.SerializeToNode(project)!;
            var image = json["Entries"]![0]!["Images"]![0]!.AsObject();
            image.Remove("Hairstyle"); image.Remove("Outfit"); image.Remove("LookName");
            File.WriteAllText(store.FilePath, json.ToJsonString());
            var original = File.ReadAllBytes(store.FilePath);
            var loaded = store.Load(); Equal(2, loaded.FormatVersion);
            var legacy = loaded.Entries[0].Images.Single(); Equal("", legacy.Hairstyle); Equal("", legacy.Outfit); Equal("", legacy.LookName);
            SameBytes(original, store.FilePath, "loading old image metadata rewrote the manuscript");
            foreach (var field in new[] { "Hairstyle", "Outfit", "LookName" })
            {
                foreach (var value in new string?[] { "春日", null })
                {
                    image[field] = value;
                    File.WriteAllText(store.FilePath, json.ToJsonString());
                    var invalid = File.ReadAllBytes(store.FilePath);
                    Throws<InvalidDataException>(() => store.Load());
                    SameBytes(invalid, store.FilePath, "opening invalid classification rewrote the manuscript");
                }
                image.Remove(field);
            }
        }),
        ("Format 1 with populated Images is rejected without modifying its bytes", dir =>
        {
            var project = Project(); project.Entries[0].Images.Add(new EntryImage { FileName = Guid.NewGuid().ToString("N") + ".png", Title = "should require version 2" });
            var store = new ProjectStore(dir); File.WriteAllText(store.FilePath, JsonSerializer.Serialize(project));
            var original = File.ReadAllBytes(store.FilePath);
            Throws<InvalidDataException>(() => store.Load()); SameBytes(original, store.FilePath, "invalid version 1 was rewritten");
        }),
        ("Null image collections, null rows and globally repeated image IDs cannot be saved", dir =>
        {
            var images = new ProjectImageStore(dir); var image = images.Import(Source(dir, "portrait.png", Png));
            var store = new ProjectStore(dir); var project = Project(); project.Entries[0].Images.Add(image); store.Save(project);
            var original = File.ReadAllBytes(store.FilePath);
            foreach (var invalidate in new Action<NovelProject>[]
            {
                value => value.Entries[0].Images = null!,
                value => value.Entries[0].Images = [null!],
                value => value.Entries[1].Images.Add(new EntryImage { Id = image.Id, FileName = image.FileName, Title = "duplicate ID in another entry" })
            })
            {
                var current = store.Load(); invalidate(current);
                Throws<InvalidDataException>(() => store.Save(current)); SameBytes(original, store.FilePath, "invalid image metadata changed the manuscript");
            }
        }),
        ("Import copies immutable managed images and survives removal of its source", dir =>
        {
            var images = new ProjectImageStore(dir); var source = Source(dir, "portrait.PNG", Png);
            var image = images.Import(source); var firstPath = images.ResolvePath(image);
            True(Regex.IsMatch(image.FileName, "^[0-9a-fA-F]{32}\\.(png|jpg|jpeg|bmp|gif)$"), "managed image filename is not a GUID plus allowed extension");
            True(Guid.TryParseExact(image.Id, "N", out _), "image did not receive an N-format GUID ID");
            Equal(Path.Combine(dir, "assets", "images", image.FileName), firstPath);
            var changed = Png.Concat(new byte[] { 0 }).ToArray(); File.WriteAllBytes(source, changed);
            var another = images.Import(source); True(image.FileName != another.FileName, "second import overwrote the first managed filename");
            File.Delete(source);
            SameBytes(Png, images.ResolvePath(image), "first imported image was tied to its source or overwritten");
            SameBytes(changed, images.ResolvePath(another), "second import bytes changed");
            Equal(2, images.EnumerateFiles().Count);
        }),
        ("Import accepts supported image signatures and rejects mismatched or unknown sources", dir =>
        {
            var images = new ProjectImageStore(dir);
            foreach (var (name, bytes) in new (string, byte[])[]
            {
                ("valid.png", Png), ("valid.gif", Gif), ("valid.bmp", Bmp), ("valid.jpg", Jpeg), ("valid.jpeg", Jpeg)
            })
            {
                var image = images.Import(Source(dir, name, bytes)); SameBytes(bytes, images.ResolvePath(image), "supported import was not byte preserving: " + name);
            }
            var before = images.EnumerateFiles().Count;
            foreach (var (name, bytes) in new (string, byte[])[]
            {
                ("empty.png", []), ("text.png", "not an image"u8.ToArray()), ("mismatch.png", Gif), ("unknown.svg", Png), ("truncated.jpg", [0xFF])
            }) ThrowsRejected(() => images.Import(Source(dir, name, bytes)));
            ThrowsRejected(() => images.Import(Path.Combine(dir, "absent.png")));
            Equal(before, images.EnumerateFiles().Count);
        }),
        ("Oversized image sources are refused before a managed file is retained", dir =>
        {
            var source = Source(dir, "oversized.png", Png);
            using (var file = new FileStream(source, FileMode.Open, FileAccess.Write)) file.SetLength(25L * 1024 * 1024 + 1);
            var images = new ProjectImageStore(dir); ThrowsRejected(() => images.Import(source)); Equal(0, images.EnumerateFiles().Count);
            True(File.Exists(source), "rejected import removed its source");
        }),
        ("Image resolution rejects non-managed paths and missing managed files", dir =>
        {
            var images = new ProjectImageStore(dir); var outside = Source(dir, "outside.png", Png);
            foreach (var fileName in new[] { "../outside.png", "..\\outside.png", outside, "assets/images/outside.png", "plain-name.png", "https://example.invalid/a.png", "", "bad\0name.png" })
                ThrowsRejected(() => images.ResolvePath(new EntryImage { FileName = fileName }));
            ThrowsRejected(() => images.ResolvePath(new EntryImage { FileName = Guid.NewGuid().ToString("N") + ".png" }));
            SameBytes(Png, outside, "invalid resolution changed the external file");
        }),
        ("Missing current references block validation and backup without changing the manuscript", dir =>
        {
            var images = new ProjectImageStore(dir); var image = images.Import(Source(dir, "portrait.png", Png));
            var project = Project(); project.Entries[0].Images.Add(image); var store = new ProjectStore(dir); store.Save(project);
            var original = File.ReadAllBytes(store.FilePath); File.Delete(images.ResolvePath(image));
            ThrowsRejected(() => images.ValidateReferences(project));
            var zip = Path.Combine(dir, "backup.zip"); File.WriteAllBytes(zip, [1, 2, 3]);
            ThrowsRejected(() => store.CreateBackup(zip)); SameBytes([1, 2, 3], zip, "failed backup overwrote an existing destination");
            SameBytes(original, store.FilePath, "missing attachment rewrote the manuscript");
        }),
        ("CopyTo relocates current and removed historical images without depending on the source folder", dir =>
        {
            var sourceFolder = Path.Combine(dir, "original"); Directory.CreateDirectory(sourceFolder);
            var images = new ProjectImageStore(sourceFolder); var current = images.Import(Source(dir, "current.png", Png));
            var historical = images.Import(Source(dir, "historical.gif", Gif));
            var project = Project(); project.Entries[0].Images.Add(current);
            var destination = Path.Combine(dir, "copy"); images.CopyTo(destination); new ProjectStore(destination).Save(project);
            var copyImages = new ProjectImageStore(destination); Equal(2, copyImages.EnumerateFiles().Count);
            File.Delete(images.ResolvePath(current)); File.Delete(images.ResolvePath(historical));
            var loaded = new ProjectStore(destination).Load(); copyImages.ValidateReferences(loaded);
            SameBytes(Png, copyImages.ResolvePath(loaded.Entries[0].Images.Single()), "relocated current image depends on the original folder");
            SameBytes(Gif, copyImages.ResolvePath(historical), "copy omitted unreferenced historical image");
        }),
        ("CopyTo never overwrites an existing managed image with different bytes", dir =>
        {
            var original = Path.Combine(dir, "original"); Directory.CreateDirectory(original);
            var images = new ProjectImageStore(original); var image = images.Import(Source(dir, "portrait.png", Png));
            var destination = Path.Combine(dir, "copy"); var targetDirectory = Path.Combine(destination, "assets", "images"); Directory.CreateDirectory(targetDirectory);
            var target = Path.Combine(targetDirectory, image.FileName); File.WriteAllBytes(target, Gif);
            ThrowsRejected(() => images.CopyTo(destination)); SameBytes(Gif, target, "copy overwrote an immutable filename in the destination");
            SameBytes(Png, images.ResolvePath(image), "copy changed source bytes");
        }),
        ("Save conflicts preserve old manuscript backups and both saved and unsaved images", dir =>
        {
            var images = new ProjectImageStore(dir); var first = images.Import(Source(dir, "saved.png", Png));
            var project = Project(); project.Entries[0].Images.Add(first); var store = new ProjectStore(dir); store.Save(project);
            project.Title = "保存版"; store.Save(project);
            var previous = File.ReadAllBytes(store.FilePath + ".bak");
            var pending = images.Import(Source(dir, "pending.gif", Gif)); project.Entries[0].Images.Add(pending);
            var external = File.ReadAllText(store.FilePath).Replace("保存版", "外部版"); File.WriteAllText(store.FilePath, external);
            var externalBytes = File.ReadAllBytes(store.FilePath);
            Throws<ConflictException>(() => store.Save(project));
            SameBytes(externalBytes, store.FilePath, "image save conflict overwrote the external manuscript");
            SameBytes(previous, store.FilePath + ".bak", "image save conflict overwrote the last known backup");
            SameBytes(Png, images.ResolvePath(first), "image save conflict removed the previously saved image");
            SameBytes(Gif, images.ResolvePath(pending), "image save conflict removed the pending image needed for rescue");
            Equal(2, project.Entries[0].Images.Count);
        }),
        ("ZIP includes current and removed historical managed image bytes at relative paths", dir =>
        {
            var images = new ProjectImageStore(dir); var old = images.Import(Source(dir, "old.gif", Gif)); var current = images.Import(Source(dir, "current.png", Png));
            var project = Project(); project.Entries[0].Images.Add(old); var store = new ProjectStore(dir); store.Save(project);
            project.Entries[0].Images = [current]; store.Save(project);
            var zip = Path.Combine(dir, "complete.zip"); store.CreateBackup(zip);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var (image, bytes) in new[] { (old, Gif), (current, Png) })
                {
                    var entry = archive.GetEntry("assets/images/" + image.FileName); True(entry != null, "ZIP omitted managed historical/current image");
                    using var input = entry!.Open(); using var output = new MemoryStream(); input.CopyTo(output);
                    True(bytes.SequenceEqual(output.ToArray()), "ZIP changed managed image bytes");
                }
                True(archive.Entries.All(entry => !Path.IsPathRooted(entry.FullName) && !entry.FullName.Contains("..")), "ZIP contains unsafe absolute or traversal paths");
            }
            var extracted = Path.Combine(dir, "extracted"); ZipFile.ExtractToDirectory(zip, extracted);
            var restored = new ProjectStore(extracted).RestorePrevious();
            Equal(old.FileName, restored.Entries[0].Images.Single().FileName);
            SameBytes(Gif, new ProjectImageStore(extracted).ResolvePath(restored.Entries[0].Images.Single()), "ZIP history cannot resolve its image after relocation");
        }),
        ("Exports and ZIP targets cannot overwrite any path inside assets", dir =>
        {
            var images = new ProjectImageStore(dir); var image = images.Import(Source(dir, "portrait.png", Png));
            var project = Project(); project.Entries[0].Images.Add(image); var store = new ProjectStore(dir); store.Save(project);
            var imagePath = images.ResolvePath(image);
            var otherAsset = Path.Combine(dir, "assets", "other", "sentinel.dat"); Directory.CreateDirectory(Path.GetDirectoryName(otherAsset)!); File.WriteAllBytes(otherAsset, [4, 5, 6]);
            foreach (var target in new[] { imagePath, otherAsset, Path.Combine(dir, "assets", "new-export.txt") })
            {
                ThrowsRejected(() => store.ExportMarkdown(target)); ThrowsRejected(() => store.ExportText(target)); ThrowsRejected(() => store.CreateBackup(target));
            }
            SameBytes(Png, imagePath, "export overwrote an image"); SameBytes([4, 5, 6], otherAsset, "export overwrote another asset");
        }),
        ("Reference images do not leak into chapter-only text exports or prompts", dir =>
        {
            var images = new ProjectImageStore(dir); var image = images.Import(Source(dir, "portrait.png", Png)); image.Title = "私密参考图片说明";
            var project = Project(); project.Entries[0].Images.Add(image); var store = new ProjectStore(dir); store.Save(project);
            var markdown = Path.Combine(dir, "chapters.md"); var text = Path.Combine(dir, "chapters.txt"); store.ExportMarkdown(markdown); store.ExportText(text);
            foreach (var path in new[] { markdown, text })
            {
                var contents = File.ReadAllText(path); True(contents.Contains("正文内容"), "chapter text was lost");
                True(!contents.Contains(image.FileName) && !contents.Contains(image.Title), "reference image leaked into a chapter-only export");
            }
            var prompt = ProjectContent.BuildPrompt(project, project.Entries[0], "整理人物设定", true);
            True(!prompt.Contains(image.FileName) && !prompt.Contains(image.Title), "local reference image metadata was silently sent to the assistant");
        })
    ];

    // Run separately when the environment permits symbolic-link fixtures. A fixture creation failure
    // is reported as a failure, never a successful product check or an invisible skipped assertion.
    internal static readonly (string Name, Action<string> Test)[] ReparseCases =
    [
        ("Managed image directories cannot redirect resolution, import, copying or backup outside the project", dir =>
        {
            foreach (var linkedLevel in new[] { "assets", "images" })
            {
                var projectFolder = Path.Combine(dir, "project-" + linkedLevel); Directory.CreateDirectory(projectFolder);
                var store = new ProjectStore(projectFolder); store.Save(Project());
                var outside = Path.Combine(dir, "outside-" + linkedLevel); var outsideImages = Path.Combine(outside, "images"); Directory.CreateDirectory(outsideImages);
                var image = new EntryImage { FileName = Guid.NewGuid().ToString("N") + ".png" };
                var sentinel = Path.Combine(outsideImages, image.FileName); File.WriteAllBytes(sentinel, Png);
                var link = linkedLevel == "assets" ? Path.Combine(projectFolder, "assets") : Path.Combine(projectFolder, "assets", "images");
                Directory.CreateDirectory(Path.GetDirectoryName(link)!);
                CreateDirectoryLink(link, linkedLevel == "assets" ? outside : outsideImages);
                try
                {
                    var images = new ProjectImageStore(projectFolder);
                    ThrowsRejected(() => images.ResolvePath(image)); ThrowsRejected(() => images.EnumerateFiles());
                    ThrowsRejected(() => images.Import(Source(dir, "new-" + linkedLevel + ".png", Png)));
                    ThrowsRejected(() => images.CopyTo(Path.Combine(dir, "copy-" + linkedLevel)));
                    ThrowsRejected(() => store.CreateBackup(Path.Combine(dir, "backup-" + linkedLevel + ".zip")));
                    SameBytes(Png, sentinel, "directory reparse operation changed the outside sentinel");
                    Equal(1, Directory.GetFiles(outsideImages).Length);
                }
                finally { Directory.Delete(link); }
            }
        }),
        ("Managed image file links are refused without reading them into copies or backups", dir =>
        {
            var projectFolder = Path.Combine(dir, "project"); Directory.CreateDirectory(projectFolder);
            var store = new ProjectStore(projectFolder); store.Save(Project());
            var sentinel = Source(dir, "outside.png", Png); var image = new EntryImage { FileName = Guid.NewGuid().ToString("N") + ".png" };
            var imageDirectory = Path.Combine(projectFolder, "assets", "images"); Directory.CreateDirectory(imageDirectory);
            var link = Path.Combine(imageDirectory, image.FileName); CreateFileLink(link, sentinel);
            try
            {
                var images = new ProjectImageStore(projectFolder);
                ThrowsRejected(() => images.ResolvePath(image)); ThrowsRejected(() => images.EnumerateFiles());
                ThrowsRejected(() => images.CopyTo(Path.Combine(dir, "copy")));
                ThrowsRejected(() => store.CreateBackup(Path.Combine(dir, "backup.zip")));
                SameBytes(Png, sentinel, "file reparse operation changed the outside sentinel");
            }
            finally { File.Delete(link); }
        })
    ];

    private static void CreateDirectoryLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { throw new InvalidOperationException("Reparse fixture unavailable: this environment cannot create a directory symbolic link; no image safety assertions ran.", ex); }
    }

    private static void CreateFileLink(string link, string target)
    {
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { throw new InvalidOperationException("Reparse fixture unavailable: this environment cannot create a file symbolic link; no image safety assertions ran.", ex); }
    }

    private static NovelProject Project() => new()
    {
        Title = "图片回归测试",
        Entries = [new NovelEntry { Kind = "character", Title = "人设", Content = "人物资料" }, new NovelEntry { Kind = "world", Title = "场景", Content = "场景资料" }, new NovelEntry { Kind = "chapter", Content = "正文内容" }]
    };

    private static string Source(string dir, string name, byte[] bytes)
    {
        var folder = Path.Combine(dir, "import-sources"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); return path;
    }

    private static void SameBytes(byte[] expected, string path, string message) => True(expected.SequenceEqual(File.ReadAllBytes(path)), message);
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected exception " + typeof(T).Name);
    }
    private static void ThrowsRejected(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { return; }
        throw new Exception("Expected rejected unsafe or invalid image operation");
    }
}
