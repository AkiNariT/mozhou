using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mozhou.Core;
namespace Mozhou.Desktop;
public partial class MainWindow
{
    private void SaveCopyClick(object sender, RoutedEventArgs e)
    {
        if (_store == null || !CanSwitchProjects()) return;
        var dialog = new OpenFolderDialog { Title = "选择保存副本的位置（将创建新文件夹）", InitialDirectory = _store.Folder };
        if (dialog.ShowDialog(this) != true) return;
        var path = Path.Combine(dialog.FolderName, SafeFilename(_project.Title) + "-副本-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4]);
        try
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<NovelProject>(System.Text.Json.JsonSerializer.Serialize(_project))!;
            new ProjectStore(path).Save(copy);
            if (!_chatDamaged) File.WriteAllText(Path.Combine(path, "assistant-chat.json"), System.Text.Json.JsonSerializer.Serialize(_chat));
            OpenFolder(path);
            MessageBox.Show(this, "完整副本已保存，并已切换到副本继续编辑。\n原小说文件保留在原位置。\n\n" + path, "副本已保存");
        }
        catch (Exception ex) { ShowError("保存副本失败", ex); }
    }
    private void NewProjectClick(object sender, RoutedEventArgs e)
    {
        if (!CanSwitchProjects() || !Flush()) return;
        var title = Dialogs.Input(this, "新建小说", "小说名称", "我的新小说"); if (title == null) return;
        var dialog = new OpenFolderDialog { Title = "选择存放位置：将在其中创建小说文件夹", InitialDirectory = _store?.Folder ?? SettingsStore.DataRoot };
        if (dialog.ShowDialog(this) != true) return;
        string clean = SafeFilename(title).Trim().TrimEnd('.'); if (string.IsNullOrWhiteSpace(clean)) clean = "新小说";
        var folder = Path.Combine(dialog.FolderName, clean);
        if (Directory.Exists(folder)) folder += "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try { Directory.CreateDirectory(folder); new ProjectStore(folder).Save(ProjectContent.CreateEmpty(title)); OpenFolder(folder); }
        catch (Exception ex) { ShowError("创建小说失败", ex); }
    }
    private void OpenProjectClick(object sender, RoutedEventArgs e)
    {
        if (!CanSwitchProjects()) return;
        if (!Flush() && MessageBox.Show(this, "当前内容尚未保存。建议先在项目菜单中另存当前小说副本，保留全部修改。仍然重新打开或切换项目？", "尚未保存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new OpenFolderDialog { Title = "选择包含 project.mozhou.json 的小说文件夹", InitialDirectory = _store?.Folder ?? SettingsStore.DataRoot };
        if (dialog.ShowDialog(this) == true) { try { OpenFolder(dialog.FolderName); } catch (Exception ex) { ShowError("打开小说失败", ex); } }
    }
    private void ProjectMenuClick(object sender, RoutedEventArgs e) { if (sender is Button b) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; } }
    private void RenameProjectClick(object sender, RoutedEventArgs e)
    {
        if (_store == null || !Flush()) return;
        var title = Dialogs.Input(this, "修改书名", "小说名称", _project.Title); if (title == null) return;
        var description = Dialogs.Input(this, "小说简介", "一句话概括这个故事", _project.Description, true);
        _project.Title = title; if (description != null) _project.Description = description;
        ProjectTitle.Text = title; Title = title + " · 墨舟"; ProjectDescription.Text = _project.Description; MarkDirty(); Flush();
    }
    private void RevealProjectClick(object sender, RoutedEventArgs e) { if (_store != null) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _store.Folder }, UseShellExecute = true }); }
    private void ExportMarkdownClick(object sender, RoutedEventArgs e) => Export(false);
    private void ExportTextClick(object sender, RoutedEventArgs e) => Export(true);
    private void Export(bool plain)
    {
        if (_store == null) return;
        var dialog = new SaveFileDialog { Title = "导出小说正文", Filter = plain ? "纯文本|*.txt" : "Markdown|*.md", FileName = SafeFilename(_project.Title) + (plain ? ".txt" : ".md") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Flush();
            // Store retains the loaded model reference, including unsaved edits, and protects internal paths.
            if (plain) _store.ExportText(dialog.FileName); else _store.ExportMarkdown(dialog.FileName);
            MessageBox.Show(this, "已导出到：\n" + dialog.FileName, "导出完成");
        }
        catch (Exception ex) { ShowError("导出失败", ex); }
    }
    private void BackupClick(object sender, RoutedEventArgs e)
    {
        if (_store == null || !Flush()) return;
        var dialog = new SaveFileDialog { Title = "备份整本小说", Filter = "ZIP 备份|*.zip", FileName = SafeFilename(_project.Title) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip" };
        if (dialog.ShowDialog(this) == true) { try { _store.CreateBackup(dialog.FileName); MessageBox.Show(this, "备份已保存。", "墨舟"); } catch (Exception ex) { ShowError("备份失败", ex); } }
    }
    private void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (_store == null || _generating) return;
        if (MessageBox.Show(this, "将用上一次保存的版本替换当前内容。继续？", "恢复上一版本", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _saveTimer.Stop();
        try { _project = _store.RestorePrevious(); _dirty = false; ProjectTitle.Text = _project.Title; Title = _project.Title + " · 墨舟"; ProjectDescription.Text = _project.Description; RefreshList(); RefreshGraph(); SetSaveStatus("● 已恢复上一版本", false); }
        catch (Exception ex) { ShowError("恢复失败", ex); }
    }
    private static string SafeFilename(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private void HelpClick(object sender, RoutedEventArgs e) => Dialogs.Text(this, "使用墨舟", "墨舟 · 本地小说工作台\n\n1. 新建小说或打开已有小说文件夹。每本小说的数据存放在 project.mozhou.json 中，可以随整个文件夹移动。\n\n2. 在正文、人设、大纲、世界观或灵感中点击 ＋ 创建条目。编辑后 0.75 秒自动保存，Ctrl+S 立即保存。分卷、分类和标签用于整理资料。\n\n3. 人物关系页支持新增有向关系。拖动人物调整位置，双击打开人设，点击关系标签修改说明。\n\n4. 右侧点击连接 Codex。使用本机官方 Codex 的 ChatGPT 登录，共用现有额度。连接不生成内容，发送消息才开始调用。无需填写 API Key。\n\n5. 发送前可查看上下文。当前条目和勾选的设定会通过 Codex 发往云端；默认不会附带其他章节全文。回答可复制、追加到发起请求时的条目，或保存为灵感笔记。\n\n6. 项目菜单可导出正文 Markdown/TXT、创建完整 ZIP 备份、恢复上一次保存。自动备份保留最近 30 个版本；重要阶段建议另做 ZIP 备份。保存冲突时，用「另存当前小说副本」保留全部未保存内容。\n\n7. Codex 未登录时，请先在官方 Codex 应用完成 ChatGPT 登录，然后重新连接。路径检测失败时可点右上角齿轮选择 codex.exe。\n\n正文使用纯文本编辑，Markdown 标记可直接输入。默认启动先进入小说书架。书架支持搜索、新建、打开与继续编辑；顶部可返回书架。卡片菜单中的“移出书架”只移除入口，不删除文件。字数按中文字符和英文单词估算。");
}
