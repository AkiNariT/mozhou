using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Mozhou.Core;

namespace Mozhou.Desktop;

public partial class BookshelfView : UserControl
{
    private List<BookCard> _books = [];
    private double _cardWidth;

    public event Action<string>? OpenRequested;
    public event Action<string>? ForgetRequested;
    public event Action<string>? LocateRequested;
    public event Action? NewRequested;
    public event Action? OpenExistingRequested;

    public BookshelfView()
    {
        InitializeComponent();
        RefreshBooks();
    }

    public void SetBooks(IEnumerable<ShelfBook> books)
    {
        ArgumentNullException.ThrowIfNull(books);
        _books = books.OrderByDescending(book => book.LastOpenedAt)
            .ThenByDescending(book => book.UpdatedAt)
            .Select((book, index) => new BookCard(book, index == 0 && book.LastOpenedAt != default, index + 1))
            .ToList();
        RefreshBooks();
    }

    public void SetNotice(string? message)
    {
        NoticeText.Text = message ?? "";
        NoticePanel.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshBooks()
    {
        if (BooksList is null) return;
        var query = SearchBox.Text.Trim();
        var visibleBooks = query.Length == 0
            ? _books
            : _books.Where(book => book.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || book.SearchDescription.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        BooksList.ItemsSource = visibleBooks;
        TotalCountLabel.Text = $"共 {_books.Count} 本";
        ResultsLabel.Text = query.Length == 0 ? "" : $"匹配 {visibleBooks.Count} 本";
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = _books.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResultsPanel.Visibility = _books.Count > 0 && visibleBooks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BooksScroll.Visibility = visibleBooks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BooksSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var columns = width >= 1130 ? 3 : width >= 690 ? 2 : 1;
        var cardWidth = Math.Floor(width / columns);
        if (Math.Abs(_cardWidth - cardWidth) < 1) return;
        _cardWidth = cardWidth;
        var style = new Style(typeof(ContentPresenter));
        style.Setters.Add(new Setter(WidthProperty, cardWidth));
        BooksList.ItemContainerStyle = style;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshBooks();
    private void ClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void NewClick(object sender, RoutedEventArgs e) => NewRequested?.Invoke();
    private void OpenExistingClick(object sender, RoutedEventArgs e) => OpenExistingRequested?.Invoke();
    private void OpenBookClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BookCard book }) OpenRequested?.Invoke(book.Folder);
    }

    private void BookMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LocateBookClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BookCard book }) LocateRequested?.Invoke(book.Folder);
    }

    private void ForgetBookClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BookCard book }) ForgetRequested?.Invoke(book.Folder);
    }

    private sealed class BookCard
    {
        private static readonly (Brush Paper, Brush Ink)[] CoverPalette =
        [
            (BrushFor("#E0D6C2"), BrushFor("#453C33")),
            (BrushFor("#D8D9CC"), BrushFor("#38433A")),
            (BrushFor("#D6C5B7"), BrushFor("#574035")),
            (BrushFor("#394741"), BrushFor("#F1E6D1")),
            (BrushFor("#46413A"), BrushFor("#F1E6D1"))
        ];

        public BookCard(ShelfBook book, bool isRecent, int index)
        {
            Folder = book.Folder;
            Title = string.IsNullOrWhiteSpace(book.Title) ? "未命名小说" : book.Title;
            SearchDescription = book.Description ?? "";
            Description = string.IsNullOrWhiteSpace(SearchDescription) ? "尚未填写简介" : SearchDescription;
            Statistics = $"{book.ChapterCount:N0} 章  ·  {book.WordCount:N0} 字";
            UpdatedLabel = book.UpdatedAt == default ? "尚无更新时间" : $"更新于 {book.UpdatedAt.ToLocalTime():yyyy.MM.dd}";
            UpdatedTooltip = book.UpdatedAt == default ? Folder : $"最后更新：{book.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n{Folder}";
            Problem = !string.IsNullOrWhiteSpace(book.Problem) ? book.Problem
                : !book.IsAvailable ? "暂时无法读取小说，可尝试打开或重新定位。" : "";
            ProblemVisibility = Problem.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            RecentVisibility = isRecent ? Visibility.Visible : Visibility.Collapsed;
            OpenLabel = isRecent && book.IsAvailable ? "继续写作  →" : "打开小说  →";
            OpenTooltip = Problem.Length == 0 ? Folder : "尝试打开小说；若有可用备份，将进入恢复流程。";
            IndexLabel = index.ToString("00", CultureInfo.InvariantCulture);
            CoverOpenName = "打开小说：" + Title;
            var titleLength = new StringInfo(Title).LengthInTextElements;
            CoverTitleFontSize = titleLength > 28 ? 16 : titleLength > 16 ? 19 : 24;
            uint hash = 2166136261;
            foreach (var character in Folder) hash = unchecked((hash ^ character) * 16777619);
            (CoverBrush, CoverInkBrush) = CoverPalette[hash % (uint)CoverPalette.Length];
        }

        public string Folder { get; }
        public string Title { get; }
        public string SearchDescription { get; }
        public string Description { get; }
        public string Statistics { get; }
        public string UpdatedLabel { get; }
        public string UpdatedTooltip { get; }
        public string Problem { get; }
        public Visibility ProblemVisibility { get; }
        public Visibility RecentVisibility { get; }
        public string OpenLabel { get; }
        public string OpenTooltip { get; }
        public string IndexLabel { get; }
        public string CoverOpenName { get; }
        public double CoverTitleFontSize { get; }
        public double CoverTitleLineHeight => CoverTitleFontSize + 7;
        public Brush CoverBrush { get; }
        public Brush CoverInkBrush { get; }

        private static Brush BrushFor(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
