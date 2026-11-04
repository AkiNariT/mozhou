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
    private bool _isCharacter;
    private bool _updatingFilters;

    public event Action? AddRequested;
    public event Action<EntryImage>? PreviewRequested;
    public event Action<EntryImage>? RemoveRequested;
    public event Action<EntryImage>? EditLookRequested;
    public string ActiveLookName => (LookFilter.SelectedItem as FilterOption)?.Value ?? "";
    public string ActiveHairstyle => (HairstyleFilter.SelectedItem as FilterOption)?.Value ?? "";
    public string ActiveOutfit => (OutfitFilter.SelectedItem as FilterOption)?.Value ?? "";

    internal (string LookName, string Hairstyle, string Outfit) GetImportLook()
    {
        string name = ActiveLookName;
        if (name.Length == 0 || _entryImages == null) return (name, ActiveHairstyle, ActiveOutfit);
        var shared = CharacterLooks.SharedTraits(_entryImages, name);
        return (name, (HairstyleFilter.SelectedItem as FilterOption)?.Value ?? shared.Hairstyle,
            (OutfitFilter.SelectedItem as FilterOption)?.Value ?? shared.Outfit);
    }

    public ReferenceImagesView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (ImagesExpander.IsExpanded) LoadThumbnails(); };
        Unloaded += (_, _) => ReleaseThumbnails();
    }

    public void SetImages(ProjectImageStore? storage, IReadOnlyList<EntryImage> images, bool isCharacter = false)
    {
        ArgumentNullException.ThrowIfNull(images);
        bool sameEntry = ReferenceEquals(_storage, storage)
            && ReferenceEquals(_entryImages, images) && _isCharacter == isCharacter;
        bool wasEmpty = !ImagesExpander.IsEnabled;
        _storage = storage;
        _entryImages = images;
        _isCharacter = isCharacter;
        LookFilters.Visibility = isCharacter ? Visibility.Visible : Visibility.Collapsed;
        ImageStrip.Height = isCharacter ? 155 : 130;
        _updatingFilters = true;
        try
        {
            SetFilterOptions(LookFilter, isCharacter ? images.Select(image => image.LookName) : null, sameEntry, "全部造型", "未命名");
            SetFilterOptions(HairstyleFilter, isCharacter ? images.Select(image => image.Hairstyle) : null, sameEntry);
            SetFilterOptions(OutfitFilter, isCharacter ? images.Select(image => image.Outfit) : null, sameEntry);
        }
        finally { _updatingFilters = false; }
        RefreshCards();
        AddButton.IsEnabled = storage != null;
        ImagesExpander.IsEnabled = images.Count > 0;
        if (images.Count == 0) ImagesExpander.IsExpanded = false;
        else if (!sameEntry || wasEmpty) ImagesExpander.IsExpanded = true;
    }

    private static void SetFilterOptions(ComboBox box, IEnumerable<string>? values, bool keepSelection,
        string allLabel = "全部", string emptyLabel = "未填写")
    {
        var previous = keepSelection ? box.SelectedItem as FilterOption : null;
        if (values == null) { box.ItemsSource = null; return; }
        List<FilterOption> options = [new(null, allLabel), new("", emptyLabel)];
        foreach (var value in values.Select(CleanLabel).Where(value => value.Length != 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCulture))
        {
            string label = value == allLabel || value == emptyLabel || value.StartsWith('「') ? $"「{value}」" : value;
            options.Add(new FilterOption(value, label));
        }
        box.ItemsSource = options;
        box.SelectedItem = options.FirstOrDefault(option => string.Equals(option.Value, previous?.Value, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }

    private void LookFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFilters) RefreshCards();
    }

    private void RefreshCards()
    {
        if (_entryImages == null) return;
        ReleaseThumbnails();
        var look = LookFilter.SelectedItem as FilterOption;
        var hair = HairstyleFilter.SelectedItem as FilterOption;
        var outfit = OutfitFilter.SelectedItem as FilterOption;
        _cards = _entryImages.Where(image => (look?.Matches(image.LookName) ?? true)
                && (hair?.Matches(image.Hairstyle) ?? true)
                && (outfit?.Matches(image.Outfit) ?? true))
            .Select(image => new ImageCard(image, _isCharacter)).ToList();
        ImageList.ItemsSource = _cards;
        bool filtered = look?.Value != null || hair?.Value != null || outfit?.Value != null;
        HeaderText.Text = filtered ? $"参考图 · {_cards.Count} / {_entryImages.Count}" : $"参考图 · {_entryImages.Count}";
        bool noMatches = filtered && _cards.Count == 0;
        NoMatches.Visibility = noMatches ? Visibility.Visible : Visibility.Collapsed;
        ImageStrip.Visibility = noMatches ? Visibility.Collapsed : Visibility.Visible;
        ImageStrip.ScrollToLeftEnd();
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
    private void EditLookClick(object sender, RoutedEventArgs e)
    {
        if (_isCharacter && sender is Button { Tag: EntryImage image }) EditLookRequested?.Invoke(image);
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

    private static string CleanLabel(string? value) => value?.Trim() ?? "";

    private sealed record FilterOption(string? Value, string Label)
    {
        public bool Matches(string? label) => Value == null || string.Equals(Value, CleanLabel(label), StringComparison.OrdinalIgnoreCase);
        public override string ToString() => Label;
    }

    private sealed class ImageCard(EntryImage image, bool isCharacter) : INotifyPropertyChanged
    {
        public EntryImage Image { get; } = image;
        public string Title => string.IsNullOrWhiteSpace(Image.Title) ? "参考图片" : Image.Title;
        public Visibility LookVisibility => isCharacter ? Visibility.Visible : Visibility.Collapsed;
        public string LookActionLabel => string.IsNullOrWhiteSpace(Image.LookName) ? "造型起名" : "编辑造型";
        public string LookSummary => !string.IsNullOrWhiteSpace(Image.LookName) ? Image.LookName : (CleanLabel(Image.Hairstyle), CleanLabel(Image.Outfit)) switch
        {
            ("", "") => "未分类",
            (var hair, "") => $"发型 · {hair}",
            ("", var outfit) => $"衣着 · {outfit}",
            (var hair, var outfit) => $"{hair} · {outfit}"
        };
        public string LookDetails => $"造型：{(string.IsNullOrWhiteSpace(Image.LookName) ? "未命名" : Image.LookName)}\n"
            + $"发型：{(string.IsNullOrWhiteSpace(Image.Hairstyle) ? "未填写" : Image.Hairstyle)}\n"
            + $"衣着：{(string.IsNullOrWhiteSpace(Image.Outfit) ? "未填写" : Image.Outfit)}";
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
