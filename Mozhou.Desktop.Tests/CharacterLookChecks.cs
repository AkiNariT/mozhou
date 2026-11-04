using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class CharacterLookChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "looks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var front = Path.Combine(root, "正面.png");
        var side = Path.Combine(root, "侧面.png");
        ReferenceImageChecks.WriteImage(front, 12, 8);
        ReferenceImageChecks.WriteImage(side, 8, 12);
        var document = Path.Combine(root, "人物.mozhou");
        var copy = Path.Combine(root, "副本.mozhou");
        CreateLegacyDocument(document, File.ReadAllBytes(front));
        var window = new MainWindow(null);
        var entryList = (ListBox)window.FindName("EntryList");
        var gallery = (ReferenceImagesView)window.FindName("ReferenceImages");
        var images = (ItemsControl)gallery.FindName("ImageList");
        var look = (ComboBox)gallery.FindName("LookFilter");
        var hair = (ComboBox)gallery.FindName("HairstyleFilter");
        var outfit = (ComboBox)gallery.FindName("OutfitFilter");
        var filters = (FrameworkElement)gallery.FindName("LookFilters");

        object? Invoke(string method, params object[] args) =>
            typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        bool Import(params string[] paths) => (bool)Invoke("AddReferenceImages", (object)paths)!;
        bool Assign(EntryImage image, string name, string hairstyle, string clothing) =>
            (bool)Invoke("ApplyImageLook", image, name, hairstyle, clothing)!;
        void CheckImportedLook(NovelEntry entry, string name, string hairstyle, string clothing, string reason)
        {
            check(Import(side), "import a reference to check " + reason);
            var added = entry.Images[^1];
            check((added.LookName, added.Hairstyle, added.Outfit) == (name, hairstyle, clothing), reason);
            // Each case starts with the same two source pictures in the named group.
            check((bool)Invoke("RemoveReferenceImage", added)!, "remove temporary reference after checking " + reason);
        }
        NovelEntry Navigate(string section)
        {
            var navigation = (Panel)window.FindName("NavStack");
            var button = navigation.Children.OfType<Button>().Single(button => Equals(button.Tag, section));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return (NovelEntry)entryList.SelectedItem;
        }
        bool Shows(params EntryImage[] expected)
        {
            var visible = images.Items.Cast<object>()
                .Select(card => (EntryImage)card.GetType().GetProperty("Image")!.GetValue(card)!);
            return visible.SequenceEqual(expected);
        }

        try
        {
            Invoke("OpenFolder", document);
            var person = Navigate("character");
            var legacy = person.Images.Single();
            check(filters.Visibility == Visibility.Visible && Shows(legacy), "character gallery includes a legacy reference without look fields");
            check(Import(front, side), "import two different looks on one character");
            var portrait = person.Images[1];
            var profile = person.Images[2];
            const string school = "入学第一天";
            check(Assign(portrait, school, "长发", "校服") && Assign(profile, school, "长发", "校服"), "set up two views of the same named look");
            Select(look, school);
            CheckImportedLook(person, school, "长发", "校服", "selecting only the look reuses its common hairstyle and outfit");
            outfit.SelectedIndex = 1;
            CheckImportedLook(person, school, "长发", "", "explicitly unspecified outfit overrides a shared outfit");
            outfit.SelectedIndex = 0;
            check(Assign(profile, school, "长发", "制服"), "vary one outfit in the named group");
            CheckImportedLook(person, school, "长发", "", "mixed outfits inherit only the common hairstyle");
            check(Assign(portrait, school, "长发", "披风") && Assign(profile, school, "短发", "制服"),
                "two references can share a custom look name while retaining distinct hairstyle and outfit");
            CheckImportedLook(person, school, "", "", "mixed hairstyles and outfits inherit only the name");
            look.SelectedIndex = 1;
            hair.SelectedIndex = 1;
            outfit.SelectedIndex = 1;
            check(Shows(legacy), "legacy reference appears under unnamed and unspecified filters");
            look.SelectedIndex = 0;
            hair.SelectedIndex = 0;
            outfit.SelectedIndex = 0;
            check(Assign(legacy, "儿时", "", ""), "custom name can be saved without hairstyle or outfit");
            Select(look, school);
            check(Shows(portrait, profile), "custom name groups both pictures regardless of hairstyle and outfit");
            look.SelectedIndex = 0;
            Select(hair, "长发");
            check(Shows(portrait), "hairstyle alone selects the matching reference");
            Select(outfit, "制服");
            check(Shows(), "incompatible hairstyle and outfit show no references");
            Select(outfit, "披风");
            check(Shows(portrait), "matching hairstyle and outfit show their intersection");
            Select(look, "儿时");
            check(Shows(), "selected custom name is also required when hairstyle and outfit match");
            Select(look, school);
            check(Import(side), "import into a filtered character gallery");
            var imported = person.Images[^1];
            check(imported.LookName == school && imported.Hairstyle == "长发" && imported.Outfit == "披风" && Shows(portrait, imported),
                "new reference inherits the selected custom name, hairstyle and outfit and stays visible");
            look.SelectedIndex = 0;
            hair.SelectedIndex = 0;
            outfit.SelectedIndex = 0;
            check(Shows(legacy, portrait, profile, imported), "clearing all filters restores the full gallery");

            Select(look, school);
            Select(hair, "长发");
            Select(outfit, "披风");
            entryList.SelectedIndex = 1;
            var other = (NovelEntry)entryList.SelectedItem;
            check(look.SelectedIndex == 0 && hair.SelectedIndex == 0 && outfit.SelectedIndex == 0 && Shows()
                && !Options(look).Contains(school) && !Options(hair).Contains("长发") && !Options(outfit).Contains("披风"),
                "switching characters clears filters and previous character options");
            check(!Assign(portrait, "误改", "误改", "误改") && portrait.LookName == school && portrait.Hairstyle == "长发" && portrait.Outfit == "披风",
                "cannot classify a reference owned by the previous character");
            check(Import(front) && Assign(other.Images.Single(), "初次登台", "卷发", "礼服"), "second character has independent look labels");
            entryList.SelectedIndex = 0;
            check(!Options(look).Contains("初次登台") && !Options(hair).Contains("卷发") && !Options(outfit).Contains("礼服") && Shows(legacy, portrait, profile, imported),
                "returning to first character restores only its pictures and labels");
            Select(look, school);
            Select(hair, "短发");
            var place = Navigate("world");
            check(Import(front) && filters.Visibility == Visibility.Collapsed && Shows(place.Images.Single()),
                "non-character entries retain the ordinary reference gallery");
            var scene = place.Images.Single();
            check(!Assign(scene, "误改", "误改", "误改") && scene.LookName == "" && scene.Hairstyle == "" && scene.Outfit == "",
                "non-character picture cannot inherit or receive character labels");

            Invoke("SaveProjectCopy", copy);
            var expectedBytes = File.ReadAllBytes(side);
            File.Delete(front);
            File.Delete(side);
            foreach (var path in new[] { document, copy })
            {
                Invoke("OpenFolder", path);
                var reopened = Navigate("character");
                Select(look, "儿时");
                var namedOnly = reopened.Images.Single(image => image.Id == legacy.Id);
                check(Shows(namedOnly) && namedOnly.LookName == "儿时" && namedOnly.Hairstyle == "" && namedOnly.Outfit == "",
                    "name-only look survives reopening " + Path.GetFileName(path));
                Select(look, school);
                var tagged = reopened.Images.Single(image => image.Id == portrait.Id);
                var secondView = reopened.Images.Single(image => image.Id == profile.Id);
                var inherited = reopened.Images.Single(image => image.Id == imported.Id);
                check(Shows(tagged, secondView, inherited), "named group survives reopening " + Path.GetFileName(path));
                Select(hair, "长发");
                Select(outfit, "披风");
                check(Shows(tagged, inherited) && secondView.Hairstyle == "短发" && secondView.Outfit == "制服",
                    "individual labels survive reopening " + Path.GetFileName(path));
                var bytes = new SingleFileDocument(path).ReadFile("assets/images/" + inherited.FileName);
                check(bytes != null && bytes.SequenceEqual(expectedBytes), "classified reference bytes travel inside " + Path.GetFileName(path));
            }

            Invoke("OpenFolder", document);
            var editing = Navigate("character");
            var currentImage = editing.Images.Single(image => image.Id == portrait.Id);
            const string draft = "毕业集会的人设补充，尚未保存。";
            ((TextBox)window.FindName("ContentBox")).Text = draft;
            using (var external = new ProjectStore(document))
            {
                var manuscript = external.Load();
                manuscript.Title = "另一窗口的稿件";
                external.Save(manuscript);
            }
            check(!Assign(currentImage, "毕业集会", "盘发", "礼服"), "confirmed look reports a save conflict with an external manuscript edit");
            check((currentImage.LookName, currentImage.Hairstyle, currentImage.Outfit) == ("毕业集会", "盘发", "礼服"),
                "confirmed look remains in the window alongside the pending character notes after a conflict");
            var recovery = Path.Combine(root, "冲突抢救副本.mozhou");
            Invoke("SaveProjectCopy", recovery);
            using (var reader = new ProjectStore(recovery))
            {
                var recovered = reader.Load().Entries.Single(entry => entry.Id == editing.Id);
                var savedImage = recovered.Images.Single(image => image.Id == currentImage.Id);
                check(recovered.Content == draft && (savedImage.LookName, savedImage.Hairstyle, savedImage.Outfit) == ("毕业集会", "盘发", "礼服"),
                    "save-as after conflict preserves both the pending character notes and the confirmed look");
            }
            using (var reader = new ProjectStore(document))
            {
                var source = reader.Load();
                var original = source.Entries.Single(entry => entry.Id == editing.Id);
                var sourceImage = original.Images.Single(image => image.Id == currentImage.Id);
                check(source.Title == "另一窗口的稿件" && original.Content == "" && (sourceImage.LookName, sourceImage.Hairstyle, sourceImage.Outfit) == (school, "长发", "披风"),
                    "recovering the local look leaves the externally edited source unchanged");
            }
        }
        finally
        {
            // The conflict is deliberate. Closing a failed test must not open a discard-changes dialog.
            typeof(MainWindow).GetField("_dirty", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            window.Close();
        }
    }

    private static IEnumerable<string?> Options(ComboBox filter) => filter.Items.Cast<object>().Select(option => option.ToString());
    private static void Select(ComboBox filter, string label) =>
        filter.SelectedItem = filter.Items.Cast<object>().Single(option => option.ToString() == label);

    private static void CreateLegacyDocument(string path, byte[] picture)
    {
        const string filename = "12345678123456781234567812345678.png";
        var manuscript = $$"""
            { "FormatVersion": 2, "Id": "looks", "Title": "人物", "Relations": [], "Entries": [
                { "Id": "first", "Kind": "character", "Title": "角色甲", "Images": [
                    { "Id": "old", "FileName": "{{filename}}", "Title": "旧照" }] },
                { "Id": "second", "Kind": "character", "Title": "角色乙" },
                { "Id": "world", "Kind": "world", "Title": "港口" }
            ] }
            """;
        SingleFileDocument.CreateNew(path, new Dictionary<string, byte[]> {
            ["project.mozhou.json"] = Encoding.UTF8.GetBytes(manuscript),
            ["assets/images/" + filename] = picture
        });
    }
}
