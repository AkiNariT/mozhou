using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class BookshelfNavigationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "bookshelf-" + Guid.NewGuid().ToString("N"));
        var firstFolder = Path.Combine(root, "first");
        var secondFolder = Path.Combine(root, "same-id-copy");
        var libraryFolder = Path.Combine(AppContext.BaseDirectory, "Library", "migration-check-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "mozhou-settings.json");
        var indexPath = Path.Combine(AppContext.BaseDirectory, "bookshelf.json");
        var originalSettings = ReadIfExists(settingsPath);
        var originalIndex = ReadIfExists(indexPath);
        var windows = new List<MainWindow>();

        MainWindow NewWindow()
        {
            var window = new MainWindow(null);
            windows.Add(window);
            return window;
        }

        try
        {
            var first = new NovelProject
            {
                Id = "shared-project-id", Title = "Shelf first book",
                Entries = [new NovelEntry { Id = "shared-entry-id", Kind = "chapter", Title = "First chapter", Content = "FIRST original manuscript" }]
            };
            var second = JsonSerializer.Deserialize<NovelProject>(JsonSerializer.Serialize(first))!;
            second.Title = "Shelf copied book";
            second.Entries[0].Content = "SECOND original manuscript";
            var firstStore = new ProjectStore(firstFolder);
            var secondStore = new ProjectStore(secondFolder);
            firstStore.Save(first);
            secondStore.Save(second);
            File.WriteAllText(Path.Combine(firstFolder, "assistant-chat.json"), JsonSerializer.Serialize(new[]
            {
                new ChatRecord { Role = "assistant", Text = "FIRST private chat", EntryId = "shared-entry-id" }
            }));
            File.WriteAllText(Path.Combine(secondFolder, "assistant-chat.json"), JsonSerializer.Serialize(new[]
            {
                new ChatRecord { Role = "assistant", Text = "SECOND private chat", EntryId = "shared-entry-id" }
            }));

            // A previous version may store the project filename rather than its directory.
            SettingsStore.Write(new UserSettings { LastProjectPath = firstStore.FilePath });
            File.Delete(indexPath);
            var window = NewWindow();
            Invoke(window, "InitializeProject", (object?)null);
            check(Field(window, "_store") == null, "default startup waits for book selection instead of reopening legacy book");
            CheckView(window, shelf: true, check);
            check(IndexContainsPath(indexPath, firstFolder), "startup migrates legacy project-file path to bookshelf folder");

            check((bool)Invoke(window, "TryOpenShelfBook", firstFolder)!, "bookshelf opens first novel");
            CheckView(window, shelf: false, check);
            check(Box(window, "ContentBox").Text == "FIRST original manuscript", "opening shelf item loads its manuscript");
            Box(window, "ContentBox").Text = "FIRST manuscript edited before returning home";
            Box(window, "PromptBox").Text = "FIRST unsent draft";
            check((bool)Invoke(window, "TryShowBookshelf")!, "returning home succeeds after saving dirty manuscript");
            CheckView(window, shelf: true, check);
            check(new ProjectStore(firstFolder).Load().Entries[0].Content == "FIRST manuscript edited before returning home", "home transition persists latest editor text");
            check(!(bool)Field(window, "_dirty")!, "home transition leaves no pending manuscript save");
            check(Field(window, "_store") == null, "home transition detaches active project so forgotten records stay forgotten");

            // Change the other file after startup: shelf navigation must read current disk data.
            second.Entries[0].Content = "SECOND manuscript updated on disk";
            secondStore.Save(second);
            check((bool)Invoke(window, "TryOpenShelfBook", secondFolder)!, "bookshelf opens a distinct folder with the same project ID");
            CheckView(window, shelf: false, check);
            check(CurrentFolder(window) == secondFolder, "same-ID copy is selected by folder");
            check(Box(window, "ContentBox").Text == "SECOND manuscript updated on disk", "switching novels reads latest manuscript from disk");
            check(Box(window, "PromptBox").Text.Length == 0, "switching novels clears previous unsent prompt");
            var request = (string)Invoke(window, "BuildRequest", "Continue this book")!;
            check(request.Contains("SECOND private chat") && !request.Contains("FIRST private chat"), "same-ID copy loads only its own chat history");
            check(!request.Contains("FIRST manuscript edited before returning home"), "new book request excludes previous manuscript");
            check(IndexContainsPath(indexPath, firstFolder) && IndexContainsPath(indexPath, secondFolder), "bookshelf persists both folders even when project IDs match");

            // Direct selection is guarded too, even if invoked while the editor is visible.
            Box(window, "ContentBox").Text = "SECOND saved by direct switch";
            Box(window, "PromptBox").Text = "SECOND unsent draft";
            check((bool)Invoke(window, "TryOpenShelfBook", firstFolder)!, "direct shelf navigation saves current book before switching");
            check(new ProjectStore(secondFolder).Load().Entries[0].Content == "SECOND saved by direct switch", "direct switch preserves pending text in previous book");
            check(Box(window, "ContentBox").Text == "FIRST manuscript edited before returning home", "switch back reloads first book correctly");
            check(Box(window, "PromptBox").Text.Length == 0, "direct switch clears the previous draft prompt");

            Invoke(window, "FocusClick", window, new RoutedEventArgs());
            check((bool)Field(window, "_focus")!, "focus fixture enters focused editor mode");
            check((bool)Invoke(window, "TryShowBookshelf")!, "focused editor can return to bookshelf");
            check((bool)Invoke(window, "TryOpenShelfBook", secondFolder)!, "another novel can open after focused editor returns home");
            check(!(bool)Field(window, "_focus")!, "opening another novel exits previous focus mode");
            foreach (var name in new[] { "NavColumn", "ListColumn", "AssistantColumn" })
                check(((ColumnDefinition)window.FindName(name)).Width.Value > 0, "opening another novel restores " + name);
            foreach (var name in new[] { "NavigationPane", "ListPane", "AssistantPane" })
                check(((UIElement)window.FindName(name)).Visibility == Visibility.Visible, "opening another novel reveals " + name);

            var currentStore = Field(window, "_store");
            var currentProject = Field(window, "_project");
            var currentEntry = Field(window, "_selected");
            Box(window, "ContentBox").Text = "SECOND unsaved conflict text";
            var externalStore = new ProjectStore(secondFolder);
            var external = externalStore.Load();
            external.Entries[0].Content = "SECOND external writer text";
            externalStore.Save(external);
            var externalBytes = File.ReadAllBytes(secondStore.FilePath);
            var firstBytes = File.ReadAllBytes(firstStore.FilePath);
            check(!(bool)Invoke(window, "TryShowBookshelf")!, "save conflict blocks returning to bookshelf");
            check(!(bool)Invoke(window, "TryOpenShelfBook", firstFolder)!, "save conflict blocks selecting another book");
            CheckView(window, shelf: false, check);
            check(ReferenceEquals(currentStore, Field(window, "_store")) && ReferenceEquals(currentProject, Field(window, "_project")) && ReferenceEquals(currentEntry, Field(window, "_selected")), "conflict keeps active project and selected entry intact");
            check((bool)Field(window, "_dirty")! && Box(window, "ContentBox").Text == "SECOND unsaved conflict text", "conflict keeps dirty editor text available");
            check(File.ReadAllBytes(secondStore.FilePath).SequenceEqual(externalBytes), "blocked conflict navigation preserves external manuscript");
            check(File.ReadAllBytes(firstStore.FilePath).SequenceEqual(firstBytes), "blocked conflict navigation does not alter target novel");

            // Reload only for the next independent scenario, with deliberate test-only cleanup.
            SetField(window, "_dirty", false);
            Invoke(window, "OpenFolder", secondFolder);
            foreach (var busyField in new[] { "_generating", "_connecting" })
            {
                var before = File.ReadAllBytes(secondStore.FilePath);
                var activeStore = Field(window, "_store");
                Box(window, "ContentBox").Text = "Pending text during " + busyField;
                SetField(window, busyField, true);
                try
                {
                    check(!(bool)Invoke(window, "TryShowBookshelf")!, busyField + " blocks returning home");
                    check(!(bool)Invoke(window, "TryOpenShelfBook", firstFolder)!, busyField + " blocks book selection");
                    CheckView(window, shelf: false, check);
                    check(ReferenceEquals(activeStore, Field(window, "_store")) && (bool)Field(window, "_dirty")!, busyField + " preserves active dirty project");
                    check(Box(window, "ContentBox").Text == "Pending text during " + busyField, busyField + " preserves editor contents");
                    check(File.ReadAllBytes(secondStore.FilePath).SequenceEqual(before), busyField + " guard does not flush or overwrite manuscript");
                }
                finally { SetField(window, busyField, false); }
                check((bool)Invoke(window, "Flush")!, "book remains saveable after " + busyField + " finishes");
            }

            foreach (var initial in new[] { firstFolder, secondStore.FilePath })
            {
                var explicitWindow = NewWindow();
                Invoke(explicitWindow, "InitializeProject", initial);
                CheckView(explicitWindow, shelf: false, check);
                check(CurrentFolder(explicitWindow) == (initial == firstFolder ? firstFolder : secondFolder), "explicit startup path opens requested novel: " + Path.GetFileName(initial));
            }

            // A valid novel title can also be the conventional manuscript filename.
            // Opening through a folder picker succeeds; the shelf must retain that directory identity.
            var filenameFolder = Path.Combine(root, "project.mozhou.json");
            var filenameProject = new NovelProject
            {
                Title = "project.mozhou.json",
                Entries = [new NovelEntry { Kind = "chapter", Title = "Filename chapter", Content = "NOVEL inside filename-shaped directory" }]
            };
            new ProjectStore(filenameFolder).Save(filenameProject);
            var filenameWindow = NewWindow();
            Invoke(filenameWindow, "OpenFolder", filenameFolder);
            check((bool)Invoke(filenameWindow, "TryShowBookshelf")!, "filename-shaped novel directory can return to shelf");
            check((bool)Invoke(filenameWindow, "TryOpenShelfBook", filenameFolder)!, "shelf reopens real directory named project.mozhou.json");
            check(CurrentFolder(filenameWindow) == filenameFolder && Box(filenameWindow, "ContentBox").Text == "NOVEL inside filename-shaped directory", "filename-shaped directory opens its own manuscript instead of parent folder");

            // Invalid settings must not suppress independently discoverable, valid Library novels.
            new ProjectStore(libraryFolder).Save(new NovelProject
            {
                Title = "Library migration survives bad legacy path",
                Entries = [new NovelEntry { Kind = "chapter", Content = "LIBRARY manuscript preserved during migration" }]
            });
            SettingsStore.Write(new UserSettings { LastProjectPath = "bad\0legacy-path" });
            File.Delete(indexPath);
            var migrationWindow = NewWindow();
            Invoke(migrationWindow, "InitializeProject", (object?)null);
            CheckView(migrationWindow, shelf: true, check);
            check(IndexContainsPath(indexPath, libraryFolder), "invalid legacy path does not block valid Library novel migration");
            check(new ProjectStore(libraryFolder).Load().Entries[0].Content == "LIBRARY manuscript preserved during migration", "migration with invalid legacy path preserves discovered manuscript");
        }
        finally
        {
            foreach (var window in windows)
            {
                ((DispatcherTimer)Field(window, "_saveTimer")!).Stop();
                SetField(window, "_dirty", false);
                SetField(window, "_generating", false);
                SetField(window, "_connecting", false);
            }
            RestoreFile(settingsPath, originalSettings);
            RestoreFile(indexPath, originalIndex);
            if (Directory.Exists(libraryFolder))
            {
                File.Delete(Path.Combine(libraryFolder, "project.mozhou.json"));
                File.Delete(Path.Combine(libraryFolder, "project.mozhou.lock"));
                Directory.Delete(libraryFolder);
            }
        }
    }

    private static void CheckView(MainWindow window, bool shelf, Action<bool, string> check)
    {
        check(window.FindName("ShelfPane") is UIElement shelfPane && shelfPane.Visibility == (shelf ? Visibility.Visible : Visibility.Collapsed), shelf ? "bookshelf is visible" : "bookshelf is hidden in editor");
        check(window.FindName("EditorShell") is UIElement editor && editor.Visibility == (shelf ? Visibility.Collapsed : Visibility.Visible), shelf ? "editor is hidden on bookshelf" : "editor is visible");
    }

    private static bool IndexContainsPath(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return Contains(document.RootElement);
        bool Contains(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Any(Contains),
            JsonValueKind.Object => value.EnumerateObject().Any(property => Contains(property.Value)),
            _ => false
        };
    }

    private static byte[]? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    private static void RestoreFile(string path, byte[]? contents) { if (contents == null) File.Delete(path); else File.WriteAllBytes(path, contents); }
    private static string? CurrentFolder(MainWindow window) => (Field(window, "_store") as ProjectStore)?.Folder;
    private static TextBox Box(MainWindow window, string name) => (TextBox)window.FindName(name);
    private static FieldInfo GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing test target field " + name);
    private static object? Field(object target, string name) => GetField(target, name).GetValue(target);
    private static void SetField(object target, string name, object? value) => GetField(target, name).SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing bookshelf behavior " + name)).Invoke(target, args);
}
