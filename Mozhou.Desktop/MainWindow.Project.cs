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
        var dialog = new SaveFileDialog
        {
            Title = "另存为完整墨舟文档（请选择新文件名）", Filter = "墨舟小说文档|*.mozhou",
            DefaultExt = ".mozhou", AddExtension = true, InitialDirectory = ProjectSaveDirectory(),
            FileName = SafeFilename(_project.Title) + (_store.IsDocument ? "-副本" : "") + ".mozhou", OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        var legacyLocation = _store.IsDocument ? null : _store.Location;
        try
        {
            ValidateNewDocumentPath(path);
            SaveProjectCopy(path);
            OpenFolder(path);
            if (legacyLocation != null)
            {
                try { _bookshelf.Forget(legacyLocation); }
                catch (Exception ex) { _shelfNotice = "新文档已保存，旧书架入口可手动移除：" + ex.Message; }
            }
            MessageBox.Show(this, "完整小说文档已保存，已切换到新文档继续编辑。\n正文、设定、关系网和图片均包含在内，原稿保留。\n\n" + path, "文档已保存");
        }
        catch (Exception ex) { ShowError("保存副本失败", ex); }
    }
    private void SaveProjectCopy(string path)
    {
        if (_store == null) throw new InvalidOperationException("请先打开小说。");
        if (path.EndsWith(".mozhou", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(path))
        {
            if (File.Exists(path)) throw new IOException("该文档已存在，请选择新的文件名，原文档不会被覆盖。");
            var chat = _chatDamaged ? null : System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_chat);
            _store.SaveDocumentCopy(path, _project, chat);
            return;
        }
        var images = _store.Images;
        images.ValidateReferences(_project);
        images.CopyTo(path);
        var copy = System.Text.Json.JsonSerializer.Deserialize<NovelProject>(System.Text.Json.JsonSerializer.Serialize(_project))!;
        using var copyStore = new ProjectStore(path);
        copyStore.Save(copy);
        if (!_chatDamaged) File.WriteAllText(Path.Combine(path, "assistant-chat.json"), System.Text.Json.JsonSerializer.Serialize(_chat));
    }

    private void NewProjectClick(object sender, RoutedEventArgs e)
    {
        if (!CanSwitchProjects() || !Flush()) return;
        var title = Dialogs.Input(this, "新建小说", "小说名称", "我的新小说"); if (title == null) return;
        string clean = SafeFilename(title).Trim().TrimEnd('.'); if (string.IsNullOrWhiteSpace(clean)) clean = "新小说";
        var dialog = new SaveFileDialog
        {
            Title = "保存新小说文档", Filter = "墨舟小说文档|*.mozhou", DefaultExt = ".mozhou", AddExtension = true,
            FileName = clean + ".mozhou", InitialDirectory = ProjectSaveDirectory(), OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ValidateNewDocumentPath(dialog.FileName);
            using var store = new ProjectStore(dialog.FileName);
            store.Save(ProjectContent.CreateEmpty(title));
            OpenFolder(dialog.FileName);
        }
        catch (Exception ex) { ShowError("创建小说失败", ex); }
    }
    private void OpenProjectClick(object sender, RoutedEventArgs e)
    {
        if (!CanSwitchProjects()) return;
        if (!Flush() && MessageBox.Show(this, "当前内容尚未保存。建议先在项目菜单中另存当前小说副本，保留全部修改。仍然重新打开或切换项目？", "尚未保存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new OpenFileDialog
        {
            Title = "打开 / 导入小说", Filter = "墨舟小说文档|*.mozhou|旧版小说主文件|project.mozhou.json",
            CheckFileExists = true, InitialDirectory = ProjectSaveDirectory()
        };
        if (dialog.ShowDialog(this) == true) { try { OpenFolder(ProjectFolder(dialog.FileName)); } catch (Exception ex) { ShowError("打开小说失败", ex); } }
    }
    private string ProjectSaveDirectory() => _store == null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : Path.GetDirectoryName(_store.Location) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static void ValidateNewDocumentPath(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".mozhou", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请使用 .mozhou 扩展名保存完整小说文档。");
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("该位置已存在文件或文件夹，请选择新的文件名，原稿不会被覆盖。");
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
    private void RevealProjectClick(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        if (_store.IsDocument) explorer.ArgumentList.Add("/select,");
        explorer.ArgumentList.Add(_store.Location);
        Process.Start(explorer);
    }
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
    private void HelpClick(object sender, RoutedEventArgs e) => Dialogs.Text(this, "使用墨舟", """
        墨舟 · 本地小说工作台

        1. 新建小说时选择保存位置，一本小说对应一个 .mozhou 文档。正文、人设、大纲、世界观、关系网、参考图、聊天和保存历史都在文档内。打开 / 导入选择该文件即可继续编辑。

        2. 旧版小说可选择 project.mozhou.json 打开，再用「项目 → 另存为墨舟文档…」转换。原文件夹保留，新文档保存成功后会直接切换过去。

        3. 在正文、人设、大纲、世界观或灵感中点击 ＋ 创建条目。编辑后 0.75 秒自动保存，Ctrl+S 立即保存。分卷、分类和标签用于整理资料。

        4. 人物关系页支持新增有向关系。拖动人物调整位置，双击打开人设，点击关系标签修改说明。

        5. 条目下方可添加多张 PNG/JPEG/BMP/GIF（首帧），点击缩略图放大。原图片保留，文档内另存一份；移除只解除关联，历史用图仍保留。单图最多 25 MiB、4000 万像素。参考图不自动发送给 Codex，也不嵌入正文导出。人设图下方可点击「造型起名」，为多张图取同一个组名，也可填写发型和衣着。选中造型名即可看整组，或同时按发型、衣着筛选。首次保存造型后，请用 0.7.0 或更新版本编辑这本小说。

        6. 项目菜单可导出正文 Markdown/TXT、创建完整 ZIP 备份、恢复上一次保存。文档内保留最近 30 个保存历史；重要阶段建议另存独立副本。保存冲突时，用「另存为墨舟文档…」保留全部未保存内容。ZIP 解压后可打开其中的 project.mozhou.json。

        7. 移动或分享前，等待“已保存到本地”并关闭小说，再复制 .mozhou 文件即可。保存过程中可能暂时出现事务文件，请勿在此时移动。通过应用打开文档；如需双击打开，可在 Windows「打开方式」中选择墨舟。本版本不自动修改系统文件关联。

        8. 右侧可连接本机官方 Codex，使用 ChatGPT 登录。发送前可查看上下文，当前条目和勾选的设定会发往云端；默认不附带其他章节全文。回答可复制、追加到发起请求时的条目，或保存为灵感。路径检测失败时可点齿轮选择 codex.exe。

        正文使用纯文本编辑，可直接输入 Markdown。默认启动进入书架；“移出书架”只移除入口，不删除文档。字数按中文字符和英文单词估算。
        """);
}
