using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mozhou.Core;
namespace Mozhou.Desktop;
public static class Dialogs
{
    public static string? Input(Window owner, string title, string caption, string initial = "", bool multiline = false)
    {
        var window = Shell(owner, title, 520, multiline ? 390 : 220);
        var panel = new DockPanel { Margin = new Thickness(24) }; window.Content = panel;
        var label = new TextBlock { Text = caption, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        var ok = new Button { Content = "保存", IsDefault = !multiline, Style = (Style)owner.FindResource("Primary") };
        var input = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
        ok.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) { input.Focus(); return; } window.DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(ok); panel.Children.Add(input);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static (string LookName, string Hairstyle, string Outfit)? EditImageLook(Window owner, NovelEntry character, EntryImage image, bool firstLook)
    {
        var window = Shell(owner, "编辑造型", 460, 400);
        var panel = new DockPanel { Margin = new Thickness(24) };
        window.Content = panel;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        actions.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        var save = new Button { Content = "保存造型", IsDefault = true, Style = (Style)owner.FindResource("Primary") };
        save.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(save);

        var fields = new StackPanel();
        panel.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var caption = $"{character.Title}  /  {image.Title}";
        fields.Children.Add(new TextBlock
        {
            Text = caption, ToolTip = caption, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)owner.FindResource("Accent"), Margin = new Thickness(0, 0, 0, 12)
        });
        var name = new TextBox { Text = image.LookName };
        var hair = new TextBox { Text = image.Hairstyle };
        var outfit = new TextBox { Text = image.Outfit };
        AddField("造型名", "为这套造型取个名字，例如 入学第一天", name, character.Images.Select(x => x.LookName), existing =>
        {
            var shared = CharacterLooks.SharedTraits(character.Images, existing);
            hair.Text = shared.Hairstyle;
            outfit.Text = shared.Outfit;
        });
        AddField("发型", "例如 长发、短发、马尾", hair, character.Images.Select(x => x.Hairstyle));
        AddField("衣着", "例如 校服、便装、礼服", outfit, character.Images.Select(x => x.Outfit));
        fields.Children.Add(new TextBlock
        {
            Text = "同名图片会归到一组。选用已有造型可带入共同的发型和衣着，各项也可留空。",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)owner.FindResource("Muted")
        });
        if (firstLook) fields.Children.Add(new TextBlock
        {
            Text = "首次保存造型后，请用墨舟 0.7.0 或更新版本打开这本小说。",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)owner.FindResource("Muted")
        });
        window.Loaded += (_, _) => name.Focus();
        return window.ShowDialog() == true ? (name.Text, hair.Text, outfit.Text) : null;

        void AddField(string label, string hint, TextBox input, IEnumerable<string> suggestions, Action<string>? onPick = null)
        {
            fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            fields.Children.Add(row);
            input.ToolTip = hint;
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            var menu = new ContextMenu();
            foreach (var value in suggestions.Select(x => x.Trim()).Where(x => x.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture))
            {
                var item = new MenuItem { Header = new TextBlock { Text = value }, ToolTip = value };
                item.Click += (_, _) => { input.Text = value; onPick?.Invoke(value); };
                menu.Items.Add(item);
            }
            if (menu.Items.Count > 0)
            {
                var pick = new Button { Content = "选用 ▾", ContextMenu = menu, Margin = new Thickness(6, 0, 0, 0),
                    Padding = new Thickness(8, 2, 8, 2), Style = (Style)owner.FindResource("Ghost") };
                System.Windows.Automation.AutomationProperties.SetName(pick, "选用已有" + label);
                menu.PlacementTarget = pick;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                pick.Click += (_, _) => menu.IsOpen = true;
                DockPanel.SetDock(pick, Dock.Right);
                row.Children.Add(pick);
            }
            row.Children.Add(input);
        }
    }

    public static void Text(Window owner, string title, string text)
    {
        var window = Shell(owner, title, 800, 650);
        var panel = new DockPanel { Margin = new Thickness(22) }; window.Content = panel;
        var close = new Button { Content = "关闭", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16), FontSize = 13 });
        window.ShowDialog();
    }
    public static bool EditRelation(Window owner, NovelProject project, CharacterRelation relation, bool creating, out bool remove)
    {
        bool delete = false;
        var people = project.Entries.Where(e => e.Kind == "character").ToList();
        var window = Shell(owner, creating ? "建立人物关系" : "编辑人物关系", 520, 540);
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = panel;
        var from = new ComboBox { ItemsSource = people, DisplayMemberPath = "Title", SelectedValuePath = "Id", SelectedValue = relation.FromId };
        var to = new ComboBox { ItemsSource = people, DisplayMemberPath = "Title", SelectedValuePath = "Id", SelectedValue = relation.ToId };
        if (from.SelectedIndex < 0) from.SelectedIndex = 0;
        if (to.SelectedIndex < 0) to.SelectedIndex = people.Count > 1 ? 1 : 0;
        void AddField(string label, Control field) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6), Foreground = Brushes.Gray, FontSize = 12 }); field.Margin = new Thickness(0, 0, 0, 14); panel.Children.Add(field); }
        AddField("关系起点", from); AddField("关系指向", to);
        var name = new TextBox { Text = relation.Label }; AddField("关系名称，例如：信任、师徒、暗恋、怀疑", name);
        var note = new TextBox { Text = relation.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 95, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; AddField("关系说明 / 发生章节", note);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (!creating) { var del = new Button { Content = "删除关系", Foreground = Brushes.IndianRed, Margin = new Thickness(0, 0, 12, 0) }; del.Click += (_, _) => { if (MessageBox.Show(window, "删除这条关系？人物档案会保留。", "删除关系", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { delete = true; window.DialogResult = true; } }; row.Children.Add(del); }
        row.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        var save = new Button { Content = "保存关系", Style = (Style)owner.FindResource("Primary") }; row.Children.Add(save);
        save.Click += (_, _) => {
            if (from.SelectedValue is not string f || to.SelectedValue is not string t || f == t || string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(window, "请选择两个不同的人物，并填写关系名称。"); return; }
            relation.FromId = f; relation.ToId = t; relation.Label = name.Text.Trim(); relation.Note = note.Text; window.DialogResult = true;
        };
        panel.Children.Add(row);
        bool accepted = window.ShowDialog() == true; remove = delete; return accepted;
    }
    private static Window Shell(Window owner, string title, double width, double height) => new() { Owner = owner, Icon = owner.Icon, Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, MinWidth = width - 80, MinHeight = height - 50, ShowInTaskbar = false };
}
