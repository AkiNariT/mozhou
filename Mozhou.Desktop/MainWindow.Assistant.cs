using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Mozhou.Codex;
using Mozhou.Core;
namespace Mozhou.Desktop;
public sealed class ChatRecord
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
    public string? EntryId { get; set; }
    public bool Complete { get; set; } = true;
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}
public partial class MainWindow
{
    private CodexClient? _codex;
    private CancellationTokenSource? _generationCancellation;
    private bool _generating, _connecting;
    private readonly List<ChatRecord> _chat = [];
    private bool _chatDamaged;

    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (_generating || _connecting) return;
        _connecting = true; ConnectButton.IsEnabled = false; SendButton.IsEnabled = false;
        ConnectionStatus.Text = "● 正在连接…"; AiStatus.Text = "正在读取账号";
        try
        {
            string? path = _settings.CodexPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) path = CodexClient.FindExecutable();
            if (path == null) throw new FileNotFoundException("未找到 Codex。请先安装官方 Codex，或在齿轮设置中选择 codex.exe。");
            await DisconnectCodexAsync();
            _codex = new CodexClient(path, Path.Combine(SettingsStore.DataRoot, ".codex-workspace"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var status = await _codex.ConnectAsync(timeout.Token);
            if (!status.IsChatGpt) throw new InvalidOperationException(status.Message);
            ConnectionStatus.Text = "● ChatGPT 已连接"; ConnectionStatus.ToolTip = status.Message;
            ConnectionStatus.Foreground = (Brush)FindResource("Accent");
            ModelPicker.ItemsSource = _codex.Models;
            ModelPicker.SelectedItem = _codex.Models.FirstOrDefault(m => m.Id == _settings.Model) ?? _codex.Models.FirstOrDefault(m => m.IsDefault) ?? _codex.Models.FirstOrDefault();
            ModelPicker.IsEnabled = true; SendButton.IsEnabled = ModelPicker.SelectedItem != null;
            ConnectButton.Content = "重新连接"; AiStatus.Text = "已就绪";
            _settings.CodexPath = path; SavePreferences();
        }
        catch (Exception ex)
        {
            await DisconnectCodexAsync();
            ConnectionStatus.Text = "● 连接未完成"; ConnectionStatus.ToolTip = ex.Message; AiStatus.Text = "请检查连接";
            AddNotice("连接失败：" + (ex is OperationCanceledException ? "连接超时，请重试。" : ex.Message));
        }
        finally { _connecting = false; ConnectButton.IsEnabled = true; }
    }

    private async Task DisconnectCodexAsync()
    {
        var client = _codex;
        _codex = null;
        ModelPicker.ItemsSource = null; ModelPicker.IsEnabled = false; SendButton.IsEnabled = false;
        if (client != null) await client.DisposeAsync();
    }

    private void CodexSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_generating || _connecting) return;
        var file = new OpenFileDialog { Title = "选择官方 Codex 可执行文件 codex.exe", Filter = "Codex 可执行文件|codex.exe|可执行文件|*.exe", FileName = _settings.CodexPath ?? CodexClient.FindExecutable() ?? "" };
        if (file.ShowDialog(this) == true) { _settings.CodexPath = file.FileName; SavePreferences(); ConnectClick(sender, e); }
    }
    private void PromptChipClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string prompt }) { PromptBox.Text = prompt; PromptBox.Focus(); } }
    private void PromptKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SendClick(sender, e); } }
    private string BuildRequest(string instruction)
    {
        var prompt = ProjectContent.BuildPrompt(_project, _selected, instruction, IncludeSettings.IsChecked == true);
        var history = _chat.Where(x => x.Complete).TakeLast(6).ToList();
        if (history.Count == 0) return prompt;
        var context = new StringBuilder();
        foreach (var message in history) context.AppendLine((message.Role == "user" ? "作者：" : "助手：") + message.Text);
        var s = context.ToString();
        if (s.Length > 12000) s = "（较早对话已截断）\n" + s[^12000..];
        return prompt + "\n\n【最近对话，仅用于衔接，不替代本次写作要求】\n" + s + "\n【本次要求再次确认】\n" + instruction;
    }
    private void ContextClick(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;
        Dialogs.Text(this, "本次将发送给 Codex 的上下文", BuildRequest(string.IsNullOrWhiteSpace(PromptBox.Text) ? "请根据当前资料协助创作。" : PromptBox.Text.Trim()));
    }
    private async void SendClick(object sender, RoutedEventArgs e)
    {
        if (_generating || _connecting || _codex == null || _store == null || !SendButton.IsEnabled) return;
        string instruction = PromptBox.Text.Trim(); if (instruction.Length == 0) { PromptBox.Focus(); return; }
        if (!Flush()) { AddNotice("当前内容尚未保存，请先处理底部提示，再发送请求。"); return; }
        string prompt = BuildRequest(instruction); string? targetId = _selected?.Id;
        string projectId = _project.Id;
        var user = new ChatRecord { Role = "user", Text = instruction, EntryId = targetId };
        _chat.Add(user); AddChatCard(user); PersistChat();
        var answer = new ChatRecord { Role = "assistant", Text = "", EntryId = targetId, Complete = false };
        var (answerBox, actions) = AddChatCard(answer);
        answerBox.Text = "正在组织思路…"; answerBox.Foreground = (Brush)FindResource("Muted");
        _generating = true; _generationCancellation = new CancellationTokenSource();
        SendButton.IsEnabled = false; ConnectButton.IsEnabled = false; ModelPicker.IsEnabled = false; CancelButton.Visibility = Visibility.Visible;
        AiStatus.Text = "生成中"; PromptBox.Clear();
        _settings.Model = (ModelPicker.SelectedItem as CodexModel)?.Id; SavePreferences();
        var hasStreamedText = false;
        using var streamed = new BufferedTextStream(batch =>
        {
            if (!hasStreamedText)
            {
                answerBox.Clear(); answerBox.Foreground = (Brush)FindResource("Ink"); hasStreamedText = true;
            }
            answerBox.AppendText(batch);
            ChatScroll.ScrollToEnd();
        });
        var streamTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        EventHandler flushStream = (_, _) => streamed.Flush();
        streamTimer.Tick += flushStream;
        string CompleteStream()
        {
            streamTimer.Stop();
            streamTimer.Tick -= flushStream;
            return streamed.Complete();
        }
        try
        {
            streamTimer.Start();
            var result = await _codex.GenerateAsync(prompt, _settings.Model, streamed.Append, _generationCancellation.Token);
            CompleteStream();
            answer.Text = result; answer.Complete = true; answerBox.Text = result; answerBox.Foreground = (Brush)FindResource("Ink");
            AddAdoptionButtons(actions, answer, projectId); AiStatus.Text = "生成完成";
        }
        catch (OperationCanceledException)
        {
            var partial = CompleteStream();
            answer.Text = partial.Length == 0 ? "本次生成已停止。" : partial + "\n\n（生成已停止，以上内容不完整。）";
            answerBox.Text = answer.Text; AiStatus.Text = "已停止";
        }
        catch (Exception ex)
        {
            var partial = CompleteStream();
            answer.Text = "本次生成未完成：" + ex.Message + (partial.Length > 0 ? "\n\n已收到的部分内容：\n" + partial : "");
            answerBox.Text = answer.Text; answerBox.Foreground = Brushes.IndianRed; AiStatus.Text = "生成未完成";
        }
        finally
        {
            streamTimer.Stop(); streamTimer.Tick -= flushStream; streamed.Dispose();
            _chat.Add(answer); PersistChat(); _generating = false; _generationCancellation.Dispose(); _generationCancellation = null;
            SendButton.IsEnabled = true; ConnectButton.IsEnabled = true; ModelPicker.IsEnabled = true; CancelButton.Visibility = Visibility.Collapsed; ChatScroll.ScrollToEnd();
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e) { _generationCancellation?.Cancel(); AiStatus.Text = "正在停止…"; }

    private (TextBox Text, StackPanel Actions) AddChatCard(ChatRecord record)
    {
        bool user = record.Role == "user";
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = user ? "作者" : "CODEX", FontSize = 11, FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 8)
        });
        var text = new TextBox
        {
            Text = record.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("Ink"), Padding = new Thickness(0),
            FontSize = 13, VerticalContentAlignment = VerticalAlignment.Top
        };
        content.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(actions);
        var card = new Border
        {
            Background = user ? (Brush)FindResource("Surface") : Brushes.Transparent,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(user ? 2 : 0),
            Padding = user ? new Thickness(12, 11, 12, 11) : new Thickness(0, 6, 0, 6),
            Margin = new Thickness(0, 6, 0, user ? 14 : 20), Child = content
        };
        ChatMessages.Children.Add(card); ChatScroll.ScrollToEnd();
        return (text, actions);
    }
    private void AddAdoptionButtons(StackPanel actions, ChatRecord answer, string projectId)
    {
        actions.Margin = new Thickness(0, 10, 0, 0);
        Button Make(string label, RoutedEventHandler handler)
        {
            var b = new Button
            {
                Content = label, Style = (Style)FindResource("Ghost"), FontSize = 11,
                Padding = new Thickness(7, 3, 7, 3), MinHeight = 26, Margin = new Thickness(0, 0, 5, 0)
            };
            b.Click += handler; actions.Children.Add(b); return b;
        }
        Make("复制", (_, _) => { try { Clipboard.SetText(answer.Text); AiStatus.Text = "已复制"; } catch (Exception ex) { AddNotice("复制失败：" + ex.Message); } });
        if (answer.EntryId != null)
            Make("追加原条目", (_, _) =>
            {
                if (_project.Id != projectId || !Flush()) return;
                var target = _project.Entries.FirstOrDefault(x => x.Id == answer.EntryId);
                if (target == null) { AddNotice("原条目已删除，请将回答另存为笔记。"); return; }
                if (MessageBox.Show(this, $"把这段回答追加到「{target.Title}」末尾？", "采用 Codex 内容", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                target.Content = target.Content.TrimEnd() + "\n\n" + answer.Text; target.UpdatedAt = DateTimeOffset.Now;
                MarkDirty(); Flush(); if (_selected?.Id == target.Id) SelectEntry(target);
            });
        Make("存为笔记", (_, _) =>
        {
            if (_project.Id != projectId || !Flush()) return;
            var title = Dialogs.Input(this, "保存为灵感笔记", "笔记标题", "Codex 灵感 " + DateTime.Now.ToString("MM-dd HH:mm")); if (title == null) return;
            var entry = ProjectContent.CreateEntry("note"); entry.Title = title; entry.Content = answer.Text; entry.Summary = "来自 Codex 的创作建议"; _project.Entries.Add(entry);
            MarkDirty(); Flush(); _section = "note"; RefreshNavigation(); RefreshList(entry.Id);
        });
    }
    private void AddNotice(string message)
    {
        ChatMessages.Children.Add(new Border
        {
            Background = (Brush)FindResource("Surface"), BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 16),
            Child = new TextBlock
            {
                Text = message, FontSize = 12, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 98, 65))
            }
        });
        ChatScroll.ScrollToEnd();
    }
    private void ClearChatForProject()
    {
        _chat.Clear(); _chatDamaged = false;
        while (ChatMessages.Children.Count > 1) ChatMessages.Children.RemoveAt(1);
        if (_store == null) return;
        var path = Path.Combine(_store.Folder, "assistant-chat.json");
        if (!File.Exists(path)) return;
        try
        {
            var history = JsonSerializer.Deserialize<List<ChatRecord>>(File.ReadAllText(path));
            if (history == null || history.Any(x => x == null || x.Text == null || x.Role is not ("user" or "assistant")))
                throw new InvalidDataException("对话记录结构无效。");
            _chat.AddRange(history.TakeLast(100));
            foreach (var message in _chat.TakeLast(40)) { var (_, actions) = AddChatCard(message); if (message.Role == "assistant" && message.Complete) AddAdoptionButtons(actions, message, _project.Id); }
        }
        catch { _chat.Clear(); _chatDamaged = true; AddNotice("对话记录无法读取，原文件已保留。本次新对话可继续使用，但不会覆盖旧记录。"); }
    }
    private void PersistChat()
    {
        if (_store == null || _chatDamaged) return;
        try
        {
            if (_chat.Count > 100) _chat.RemoveRange(0, _chat.Count - 100);
            var path = Path.Combine(_store.Folder, "assistant-chat.json"); var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_chat, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        catch (Exception ex) { AddNotice("对话记录保存失败：" + ex.Message); }
    }
}
