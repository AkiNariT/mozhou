using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mozhou.Desktop;

public sealed class ImagePreviewWindow : Window
{
    private readonly Image _image;
    private readonly ScrollViewer _scroll;
    private readonly Slider _zoom;
    private readonly TextBlock _zoomLabel;
    private bool _updatingZoom;
    private bool _fitToWindow = true;

    public ImagePreviewWindow(Window owner, string path, string title)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var bitmap = LocalImageLoader.Load(path, LocalImageLoader.PreviewPixels);
        Owner = owner;
        Icon = owner.Icon;
        Title = string.IsNullOrWhiteSpace(title) ? "参考图 · 墨舟" : title + " · 墨舟";
        Width = Math.Max(640, Math.Min(1100, SystemParameters.WorkArea.Width - 80));
        Height = Math.Max(440, Math.Min(800, SystemParameters.WorkArea.Height - 80));
        MinWidth = 560; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brush(owner, "Paper", "#FFFCF6");
        Foreground = Brush(owner, "Ink", "#2C2824");
        FontFamily = owner.FontFamily;
        FontSize = 13;
        UseLayoutRounding = true;

        var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        var heading = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "参考图片" : title,
            ToolTip = title, FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(heading);

        _image = new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brush(owner, "Surface", "#F3EFE6"), Content = _image, Padding = new Thickness(10)
        };
        var frame = new Border { BorderBrush = Brush(owner, "Line", "#DCD5C8"), BorderThickness = new Thickness(1), Child = _scroll };
        Grid.SetRow(frame, 1); root.Children.Add(frame);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var close = new Button { Content = "关闭", Padding = new Thickness(16, 6, 16, 6) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        var fit = new Button { Content = "适应窗口", Margin = new Thickness(0, 0, 16, 0), Padding = new Thickness(10, 6, 10, 6) };
        fit.Click += (_, _) => { _fitToWindow = true; FitImage(); };
        footer.Children.Add(fit);
        _zoom = new Slider { Minimum = 5, Maximum = 400, Value = 100, Width = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = "缩放图片" };
        System.Windows.Automation.AutomationProperties.SetName(_zoom, "图片缩放百分比");
        footer.Children.Add(_zoom);
        _zoomLabel = new TextBlock { MinWidth = 52, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(owner, "Muted", "#716A61") };
        footer.Children.Add(_zoomLabel);
        _zoom.ValueChanged += (_, _) => { if (!_updatingZoom) _fitToWindow = false; ApplyZoom(); };
        _scroll.SizeChanged += (_, _) => { if (_fitToWindow) FitImage(); };
        Loaded += (_, _) => FitImage();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => _image.Source = null;
    }

    private void FitImage()
    {
        if (_image.Source is not BitmapSource source) return;
        double width = Math.Max(1, _scroll.ActualWidth - 32);
        double height = Math.Max(1, _scroll.ActualHeight - 32);
        double scale = Math.Min(1, Math.Min(width / source.PixelWidth, height / source.PixelHeight));
        _updatingZoom = true;
        _zoom.Value = Math.Clamp(scale * 100, _zoom.Minimum, _zoom.Maximum);
        _updatingZoom = false;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_image.Source is not BitmapSource source) return;
        double scale = _zoom.Value / 100;
        _image.Width = source.PixelWidth * scale;
        _image.Height = source.PixelHeight * scale;
        _zoomLabel.Text = $"{_zoom.Value:0}%";
    }

    private static Brush Brush(Window owner, string key, string fallback) => owner.TryFindResource(key) as Brush
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
}
