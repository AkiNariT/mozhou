using System.IO;
using System.Windows;
using Microsoft.Win32;
using Mozhou.Core;

namespace Mozhou.Desktop;

public partial class MainWindow
{
    private readonly BookshelfStore _bookshelf = new(SettingsStore.DataRoot);
    private bool _onShelf = true;
    private string? _shelfNotice;

    private void WireBookshelf()
    {
        ShelfPane.OpenRequested += folder => TryOpenShelfBook(folder);
        ShelfPane.NewRequested += () => NewProjectClick(this, new RoutedEventArgs());
        ShelfPane.OpenExistingRequested += () => OpenProjectClick(this, new RoutedEventArgs());
        ShelfPane.ForgetRequested += ForgetShelfBook;
        ShelfPane.LocateRequested += LocateShelfBook;
    }

    private void InitializeBookshelf()
    {
        try
        {
            if (!_bookshelf.Exists)
            {
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(_settings.LastProjectPath))
                {
                    try { candidates.Add(ProjectFolder(_settings.LastProjectPath)); }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    { _shelfNotice = "旧设置中的小说路径无效，已跳过。你可以通过“打开小说文件夹”重新添加。"; }
                }
                var library = Path.Combine(SettingsStore.DataRoot, "Library");
                if (Directory.Exists(library))
                    candidates.AddRange(Directory.EnumerateDirectories(library)
                        .Where(folder => (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0
                            && (File.Exists(Path.Combine(folder, "project.mozhou.json"))
                                || File.Exists(Path.Combine(folder, "project.mozhou.json.bak")))));
                _bookshelf.Seed(candidates);
            }
        }
        catch (Exception ex) { _shelfNotice = "书架信息无法读取，原文件已保留。你仍可以打开小说文件夹。\n" + ex.Message; }
        ShowBookshelf();
    }

    private static string ProjectFolder(string path)
    {
        var folder = !Directory.Exists(path) && (File.Exists(path) || string.Equals(Path.GetFileName(path), "project.mozhou.json", StringComparison.OrdinalIgnoreCase))
            ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(folder);
    }

    private bool CanSwitchProjects()
    {
        if (!_generating && !_connecting) return true;
        string message = _generating ? "请先停止当前生成，等待结束后再切换小说。" : "正在连接 Codex，请稍候再切换小说。";
        AiStatus.Text = message;
        if (_onShelf) ShelfPane.SetNotice(message);
        else SetSaveStatus(message, true);
        return false;
    }

    private bool TryShowBookshelf()
    {
        if (!CanSwitchProjects() || !Flush()) return false;
        RememberCurrentProject();
        _saveTimer.Stop();
        _store = null; _selected = null; _project = new NovelProject(); _dirty = false;
        PromptBox.Clear(); ClearChatForProject();
        ShowBookshelf();
        return true;
    }

    private void ReturnToShelfClick(object sender, RoutedEventArgs e) => TryShowBookshelf();

    private void ShowBookshelf()
    {
        _onShelf = true;
        EditorShell.Visibility = Visibility.Collapsed;
        ShelfPane.Visibility = Visibility.Visible;
        Title = "墨舟 · 小说书架";
        RefreshBookshelf();
    }

    private void RefreshBookshelf()
    {
        try { ShelfPane.SetBooks(_bookshelf.Refresh()); ShelfPane.SetNotice(_shelfNotice); }
        catch (Exception ex)
        {
            ShelfPane.SetBooks([]);
            ShelfPane.SetNotice("书架列表暂时无法读取，小说文件仍保存在原目录。可通过“打开小说文件夹”继续写作。\n" + ex.Message);
        }
    }

    private bool TryOpenShelfBook(string folder)
    {
        if (!CanSwitchProjects() || !Flush()) return false;
        try { OpenFolder(ProjectFolder(folder)); return true; }
        catch (Exception ex)
        {
            _shelfNotice = "这本小说暂时无法打开。若文件夹已移动，可在卡片菜单中选择“重新定位”。\n" + ex.Message;
            ShelfPane.SetNotice(_shelfNotice);
            if (!_onShelf) SetSaveStatus("打开小说失败：" + ex.Message, true);
            return false;
        }
    }

    private void ShowEditor()
    {
        if (_focus) FocusClick(this, new RoutedEventArgs());
        _onShelf = false;
        ShelfPane.Visibility = Visibility.Collapsed;
        EditorShell.Visibility = Visibility.Visible;
        Title = _project.Title + " · 墨舟";
    }

    private void RememberCurrentProject()
    {
        if (_store == null || _dirty) return;
        try { _bookshelf.Remember(_store.Folder, _project); _shelfNotice = null; }
        catch (Exception ex) { _shelfNotice = "小说可以继续编辑，但书架记录尚未更新：" + ex.Message; }
    }

    private void ForgetShelfBook(string folder)
    {
        try
        {
            _bookshelf.Forget(folder);
            if (!string.IsNullOrWhiteSpace(_settings.LastProjectPath)
                && string.Equals(ProjectFolder(_settings.LastProjectPath), ProjectFolder(folder), StringComparison.OrdinalIgnoreCase))
            { _settings.LastProjectPath = null; SavePreferences(); }
            _shelfNotice = "已移出书架。小说文件仍保留在原文件夹，可随时重新打开。";
            RefreshBookshelf();
        }
        catch (Exception ex) { ShelfPane.SetNotice("未能更新书架：" + ex.Message); }
    }

    private void LocateShelfBook(string folder)
    {
        if (!CanSwitchProjects() || !Flush()) return;
        var dialog = new OpenFolderDialog
        {
            Title = "选择这本小说现在所在的文件夹",
            InitialDirectory = Directory.Exists(folder) ? folder : SettingsStore.DataRoot
        };
        if (dialog.ShowDialog(this) != true || !TryOpenShelfBook(dialog.FolderName)) return;
        if (string.Equals(ProjectFolder(folder), ProjectFolder(dialog.FolderName), StringComparison.OrdinalIgnoreCase)) return;
        try { _bookshelf.Forget(folder); }
        catch (Exception ex) { SetSaveStatus("小说已打开，但旧书架记录未移除：" + ex.Message, true); }
    }
}
