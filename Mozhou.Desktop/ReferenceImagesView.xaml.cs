using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Mozhou.Core;

namespace Mozhou.Desktop;

public partial class ReferenceImagesView : UserControl
{
    private ProjectImageStore? _storage;
    private IReadOnlyList<EntryImage>? _entryImages;
    private List<ImageCard> _cards = [];

    public event Action? AddRequested;
    public event Action<EntryImage>? PreviewRequested;
    public event Action<EntryImage>? RemoveRequested;

    public ReferenceImagesView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (ImagesExpander.IsExpanded) LoadThumbnails(); };
        Unloaded += (_, _) => ReleaseThumbnails();
    }

    public void SetImages(ProjectImageStore? storage, IReadOnlyList<EntryImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        bool sameEntry = ReferenceEquals(_storage, storage)
            && ReferenceEquals(_entryImages, images);
        bool wasEmpty = _cards.Count == 0;
        ReleaseThumbnails();
        ImageList.ItemsSource = null;
        _storage = storage;
        _entryImages = images;
        _cards = images.Select(image => new ImageCard(image)).ToList();
        ImageList.ItemsSource = _cards;
        HeaderText.Text = $"参考图 · {_cards.Count}";
        AddButton.IsEnabled = storage != null;
        ImagesExpander.IsEnabled = _cards.Count > 0;
        if (_cards.Count == 0) ImagesExpander.IsExpanded = false;
        else if (!sameEntry || wasEmpty) ImagesExpander.IsExpanded = true;
        if (ImagesExpander.IsExpanded) LoadThumbnails();
    }

    private void ImagesExpanded(object sender, RoutedEventArgs e) => LoadThumbnails();
    private void ImagesCollapsed(object sender, RoutedEventArgs e) => ReleaseThumbnails();
    private void AddClick(object sender, RoutedEventArgs e) => AddRequested?.Invoke();
    private void PreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EntryImage image }) PreviewRequested?.Invoke(image);
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EntryImage image }) RemoveRequested?.Invoke(image);
    }

    private void LoadThumbnails()
    {
        foreach (var card in _cards)
        {
            if (card.Attempted) continue;
            card.Attempted = true;
            try
            {
                if (_storage == null) throw new InvalidDataException("尚未打开小说。");
                string path = _storage.ResolvePath(card.Image);
                card.SetResult(LocalImageLoader.Load(path), "");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
            {
                card.SetResult(null, error is FileNotFoundException or DirectoryNotFoundException ? "图片文件缺失" : "图片无法读取");
            }
        }
    }

    private void ReleaseThumbnails()
    {
        foreach (var card in _cards)
        {
            card.Attempted = false;
            card.SetResult(null, "");
        }
    }

    private sealed class ImageCard(EntryImage image) : INotifyPropertyChanged
    {
        public EntryImage Image { get; } = image;
        public string Title => string.IsNullOrWhiteSpace(Image.Title) ? "参考图片" : Image.Title;
        public BitmapSource? Thumbnail { get; private set; }
        public string Placeholder { get; private set; } = "";
        public bool CanPreview => Thumbnail != null;
        public string PreviewHint => CanPreview ? $"放大查看：{Title}" : $"{Title} · {Placeholder}";
        public bool Attempted { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetResult(BitmapSource? thumbnail, string placeholder)
        {
            Thumbnail = thumbnail;
            Placeholder = placeholder;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
