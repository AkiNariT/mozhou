using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mozhou.Core;
namespace Mozhou.Desktop;
public partial class MainWindow : Window
{
    private NovelProject _project = new();
    private ProjectStore? _store;
    private NovelEntry? _selected;
    private string _section = "chapter";
    private bool _loading, _refreshing, _dirty, _focus;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly UserSettings _settings;
    private readonly ManuscriptStatistics _statistics = new();
    private static readonly Dictionary<string, string> SectionNames = new() { ["chapter"] = "正文写作", ["character"] = "人设档案", ["graph"] = "人物关系", ["outline"] = "故事大纲", ["world"] = "世界观设定", ["note"] = "灵感笔记" };
    public MainWindow(string? initialProject)
    {
        InitializeComponent(); _settings = SettingsStore.Read(); WireBookshelf();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Flush(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Flush(); e.Handled = true; } };
        CharacterGraph.Changed += () => { MarkDirty(); Flush(); };
        CharacterGraph.CharacterOpened += entry => { if (!Flush()) return; _section = "character"; RefreshNavigation(); RefreshList(entry.Id); };
        CharacterGraph.RelationSelected += EditRelation;
        Loaded += (_, _) => InitializeProject(initialProject);
    }
    private void InitializeProject(string? initial)
    {
        InitializeBookshelf();
        if (!string.IsNullOrWhiteSpace(initial)) TryOpenShelfBook(initial);
    }
    private void OpenFolder(string folder)
    {
        var store = new ProjectStore(folder);
        NovelProject project;
        try { project = store.Load(); }
        catch (Exception ex) when ((ex is System.Text.Json.JsonException || ex is InvalidDataException || ex is FileNotFoundException) && File.Exists(store.FilePath + ".bak"))
        {
            if (MessageBox.Show(this, "小说文件无法读取，但发现了上一次保存的备份。\n是否恢复该备份？恢复前的文件会保留在 backups 文件夹中。\n\n原因：" + ex.Message, "恢复小说备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) throw;
            project = store.RestorePrevious();
        }
        _store = store; _project = project; _selected = null; _dirty = false; _section = "chapter";
        _settings.LastProjectPath = store.Folder; SavePreferences();
        ProjectTitle.Text = project.Title; ProjectDescription.Text = string.IsNullOrWhiteSpace(project.Description) ? "属于你的故事，在这里展开。" : project.Description;
        PathStatus.Text = store.Folder; PathStatus.ToolTip = store.Folder;
        _loading = true; SearchBox.Text = ""; _loading = false;
        RefreshNavigation(); RefreshList(); UpdateCounts(); SetSaveStatus("● 已保存到本地", false);
        PromptBox.Clear(); ClearChatForProject();
        RememberCurrentProject(); ShowEditor();
    }
    private void SavePreferences()
    {
        try { SettingsStore.Write(_settings); }
        catch { /* Preferences never prevent a manuscript operation. */ }
    }
    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } || !Flush()) return;
        _section = kind; _loading = true; SearchBox.Text = ""; _loading = false;
        RefreshNavigation(); RefreshList();
    }
    private void RefreshNavigation()
    {
        foreach (var button in NavStack.Children.OfType<Button>())
        {
            bool selected = (string?)button.Tag == _section;
            button.Background = selected ? (Brush)FindResource("Selection") : Brushes.Transparent;
            button.Foreground = (Brush)FindResource(selected ? "Accent" : "Muted");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
        SectionTitle.Text = _section == "graph" ? "关系中的人物" : SectionNames[_section];
        EditorPane.Visibility = _section == "graph" ? Visibility.Collapsed : Visibility.Visible;
        GraphPane.Visibility = _section == "graph" ? Visibility.Visible : Visibility.Collapsed;
        EmptyPane.Visibility = Visibility.Collapsed;
        if (_section == "graph") RefreshGraph();
    }
    private void RefreshList(string? selectedId = null)
    {
        if (EntryList == null) return;
        _refreshing = true;
        var kind = _section == "graph" ? "character" : _section;
        string q = SearchBox.Text.Trim();
        var items = _project.Entries.Where(x => x.Kind == kind && (q.Length == 0 || (x.Title + "\n" + x.Summary + "\n" + x.Tags + "\n" + x.Content).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        var entry = items.FirstOrDefault(x => x.Id == (selectedId ?? _selected?.Id)) ?? items.FirstOrDefault();
        EntryList.ItemsSource = items; EntryList.SelectedItem = entry; EntryCount.Text = $"{items.Count} 个条目";
        _refreshing = false; SelectEntry(entry);
    }
    private void EntrySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _loading) return;
        if (!Flush()) { _refreshing = true; EntryList.SelectedItem = _selected; _refreshing = false; return; }
        SelectEntry(EntryList.SelectedItem as NovelEntry);
    }
    private void SelectEntry(NovelEntry? entry)
    {
        _selected = entry; _loading = true;
        TitleBox.Text = entry?.Title ?? ""; GroupBox.Text = entry?.Group ?? ""; SummaryBox.Text = entry?.Summary ?? ""; TagsBox.Text = entry?.Tags ?? ""; ContentBox.Text = entry?.Content ?? "";
        foreach (var box in new[] { TitleBox, GroupBox, TagsBox, SummaryBox, ContentBox }) { box.IsUndoEnabled = false; box.IsUndoEnabled = true; }
        GroupLabel.Text = entry?.Kind == "character" ? "所属阵营 / 身份" : entry?.Kind == "chapter" ? "所属分卷" : "分类";
        EditorBreadcrumb.Text = $"{_project.Title}  /  {SectionNames.GetValueOrDefault(entry?.Kind ?? _section, "创作资料")}";
        _loading = false;
        if (_section != "graph") { EditorPane.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible; EmptyPane.Visibility = entry == null ? Visibility.Visible : Visibility.Collapsed; }
        UpdateCounts();
    }
    private void EditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Title = TitleBox.Text; _selected.Group = GroupBox.Text; _selected.Tags = TagsBox.Text; _selected.Summary = SummaryBox.Text; _selected.Content = ContentBox.Text; _selected.UpdatedAt = DateTimeOffset.Now;
        MarkDirty(); UpdateCounts();
    }
    private void MarkDirty() { _dirty = true; SetSaveStatus("● 有修改，正在等待保存…", false); _saveTimer.Stop(); _saveTimer.Start(); }
    private bool Flush()
    {
        _saveTimer.Stop();
        if (!_dirty || _store == null) return true;
        try
        {
            _project.UpdatedAt = DateTimeOffset.Now; _store.Save(_project); _dirty = false;
            SetSaveStatus($"● 已保存到本地 · {DateTime.Now:HH:mm:ss}", false);
            EntryList.Items.Refresh(); UpdateCounts(); return true;
        }
        catch (ConflictException ex) { SetSaveStatus("保存暂停：文件已被修改。请在「项目」中另存当前小说副本，再处理原文件。", true); SaveStatus.ToolTip = ex.Message; return false; }
        catch (Exception ex) { SetSaveStatus("保存失败：" + ex.Message, true); return false; }
    }
    private void SetSaveStatus(string text, bool error) { SaveStatus.Text = text; if (!error) SaveStatus.ToolTip = null; SaveStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(error ? "#B24832" : "#6B8271")); }
    private void UpdateCounts()
    {
        var entry = _statistics.GetEntryStatistics(_selected);
        EntryWords.Text = $"{entry.WordCount:N0} 字 · {entry.ParagraphCount} 段";
        TotalWords.Text = $"{_statistics.GetChapterWordCount(_project):N0}";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!_loading && _store != null && Flush()) RefreshList(); }
    private void AddEntryClick(object sender, RoutedEventArgs e)
    {
        if (_store == null || !Flush()) return;
        string kind = _section == "graph" ? "character" : _section;
        var entry = ProjectContent.CreateEntry(kind);
        var title = Dialogs.Input(this, "新建" + SectionNames[kind], "给这个条目取个名字", entry.Title);
        if (title == null) return;
        entry.Title = title;
        if (kind == "character") { int index = _project.Entries.Count(x => x.Kind == "character"); entry.X = 70 + (index % 3) * 235; entry.Y = 70 + (index / 3) * 180; }
        _project.Entries.Add(entry); _loading = true; SearchBox.Text = ""; _loading = false;
        MarkDirty(); Flush(); RefreshList(entry.Id); RefreshGraph(); ContentBox.Focus();
    }
    private void DeleteEntryClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _generating || !Flush()) return;
        if (MessageBox.Show(this, $"删除「{_selected.Title}」？\n人物的关联连线也会移除。可通过项目菜单恢复上一次保存。", "删除条目", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var id = _selected.Id; _project.Entries.Remove(_selected); _project.Relations.RemoveAll(r => r.FromId == id || r.ToId == id); foreach (var x in _project.Entries) x.LinkedIds.Remove(id);
        _selected = null; MarkDirty(); Flush(); RefreshList(); RefreshGraph();
    }
    private void MoveUpClick(object sender, RoutedEventArgs e) => MoveEntry(-1);
    private void MoveDownClick(object sender, RoutedEventArgs e) => MoveEntry(1);
    private void MoveEntry(int direction)
    {
        if (_selected == null || !Flush()) return;
        var same = _project.Entries.Where(x => x.Kind == _selected.Kind).ToList(); int index = same.IndexOf(_selected), next = index + direction;
        if (next < 0 || next >= same.Count) return;
        int a = _project.Entries.IndexOf(_selected), b = _project.Entries.IndexOf(same[next]);
        (_project.Entries[a], _project.Entries[b]) = (_project.Entries[b], _project.Entries[a]); MarkDirty(); Flush(); RefreshList(_selected.Id);
    }
    private void FocusClick(object sender, RoutedEventArgs e)
    {
        _focus = !_focus; NavColumn.Width = new GridLength(_focus ? 0 : 154); ListColumn.Width = new GridLength(_focus ? 0 : 216); AssistantColumn.Width = new GridLength(_focus ? 0 : 316);
        NavigationPane.Visibility = ListPane.Visibility = AssistantPane.Visibility = _focus ? Visibility.Collapsed : Visibility.Visible; FocusButton.Content = _focus ? "退出专注" : "专注写作";
    }
    private void ShowError(string title, Exception ex) { SetSaveStatus(title + "：" + ex.Message, true); MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!Flush() && MessageBox.Show(this, "保存尚未成功。关闭将丢失窗口内未保存的修改。仍然关闭？", "尚未保存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        RememberCurrentProject();
        _saveTimer.Stop(); _generationCancellation?.Cancel(); if (_codex != null) _ = _codex.DisposeAsync();
    }
}
