using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Mozhou.Core;

namespace Mozhou.Desktop;

/// <summary>A local, editable view of persisted character positions and directed relationships.</summary>
public sealed class GraphView : UserControl
{
    private const double CardWidth = 170, CardHeight = 90, MaxCoordinate = 12000;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Width = 650, Height = 500 };
    private readonly Canvas _edges = new();
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, Border> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> _positions = new(StringComparer.Ordinal);
    private List<NovelEntry> _people = [];
    private List<CharacterRelation> _relations = [];
    private NovelEntry? _dragging;
    private Border? _dragCard;
    private Point _dragOrigin, _nodeOrigin;
    private bool _moved;

    public event Action? Changed;
    public event Action<NovelEntry>? CharacterOpened;
    public event Action<CharacterRelation>? RelationSelected;

    public GraphView()
    {
        _scroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Padding = new Thickness(0)
        };
        Content = _scroll;
        SizeChanged += (_, _) => ResizeCanvas();
    }

    public void Render(NovelProject project, string? focusId = null)
    {
        EndDrag();
        _canvas.Children.Clear(); _cards.Clear(); _positions.Clear();
        var all = project.Entries.Where(e => e.Kind == "character")
            .GroupBy(e => e.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        var visible = new HashSet<string>(all.Select(e => e.Id), StringComparer.Ordinal);
        if (focusId != null && visible.Contains(focusId))
        {
            visible.Clear(); visible.Add(focusId);
            foreach (var relation in project.Relations)
            {
                if (relation.FromId == focusId) visible.Add(relation.ToId);
                if (relation.ToId == focusId) visible.Add(relation.FromId);
            }
        }
        _people = all.Where(e => visible.Contains(e.Id)).ToList();
        var realIds = new HashSet<string>(_people.Select(e => e.Id), StringComparer.Ordinal);
        _relations = project.Relations.Where(r => r.FromId != r.ToId && realIds.Contains(r.FromId) && realIds.Contains(r.ToId)).ToList();
        _canvas.Children.Add(_edges);
        if (_people.Count == 0)
        {
            var empty = new StackPanel { Width = 440 };
            empty.Children.Add(new TextBlock { Text = "故事中的人物，将在这里相遇。", FontSize = 20, FontWeight = FontWeights.Medium, Foreground = Brush("#2C2824"), TextAlignment = TextAlignment.Center });
            empty.Children.Add(new TextBlock { Text = "点击左侧 ＋ 新建人物，再用“建立关系”连接他们。", FontSize = 12, Foreground = Brush("#716A61"), Margin = new Thickness(0, 16, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            Canvas.SetLeft(empty, 95); Canvas.SetTop(empty, 190); _canvas.Children.Add(empty);
        }
        for (int i = 0; i < _people.Count; i++)
        {
            var entry = _people[i];
            var position = new Point(Coordinate(entry.X, 60 + i % 3 * 235), Coordinate(entry.Y, 60 + i / 3 * 180));
            _positions[entry.Id] = position;
            var card = CreateCard(entry, entry.Id == focusId);
            _cards[entry.Id] = card; _canvas.Children.Add(card);
            Canvas.SetLeft(card, position.X); Canvas.SetTop(card, position.Y);
        }
        DrawRelations(); ResizeCanvas();
    }

    private Border CreateCard(NovelEntry entry, bool focused)
    {
        var card = new Border
        {
            Width = CardWidth, Height = CardHeight,
            Background = Brush(focused ? "#EDE2D6" : "#FFFCF6"),
            BorderBrush = Brush(focused ? "#9A4A3B" : "#DCD5C8"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
            Padding = new Thickness(16, 12, 16, 10), Cursor = Cursors.Hand,
            ToolTip = $"{entry.Title}\n{entry.Summary}\n拖动调整位置 · 双击打开人物档案"
        };
        AutomationProperties.SetName(card, "人物：" + entry.Title);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = entry.Title, Foreground = Brush("#2C2824"), FontFamily = new FontFamily("SimSun"), FontSize = 18, FontWeight = FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(entry.Group) ? "人物档案" : entry.Group, Foreground = Brush("#716A61"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        card.Child = stack;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                EndDrag(); e.Handled = true; CharacterOpened?.Invoke(entry); return;
            }
            _dragging = entry; _dragCard = card; _dragOrigin = e.GetPosition(_canvas); _nodeOrigin = _positions[entry.Id]; _moved = false;
            Panel.SetZIndex(card, 2); card.CaptureMouse(); e.Handled = true;
        };
        card.MouseMove += (_, e) =>
        {
            if (_dragging != entry || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(_canvas) - _dragOrigin;
            if (!_moved && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var point = new Point(Coordinate(_nodeOrigin.X + delta.X, _nodeOrigin.X), Coordinate(_nodeOrigin.Y + delta.Y, _nodeOrigin.Y));
            if (point == _positions[entry.Id]) return;
            _moved = true; _positions[entry.Id] = point; entry.X = point.X; entry.Y = point.Y;
            Canvas.SetLeft(card, point.X); Canvas.SetTop(card, point.Y);
            DrawRelations(); ResizeCanvas(); e.Handled = true;
        };
        card.MouseLeftButtonUp += (_, e) => { if (_dragging == entry) { EndDrag(); e.Handled = true; } };
        card.LostMouseCapture += (_, _) => { if (_dragging == entry) EndDrag(); };
        return card;
    }

    private void EndDrag()
    {
        if (_dragging == null) return;
        var entry = _dragging; var card = _dragCard; bool moved = _moved;
        _dragging = null; _dragCard = null; _moved = false;
        if (card != null) { Panel.SetZIndex(card, 1); if (card.IsMouseCaptured) card.ReleaseMouseCapture(); }
        if (moved) { entry.UpdatedAt = DateTimeOffset.Now; Changed?.Invoke(); }
    }

    private void DrawRelations()
    {
        _edges.Children.Clear();
        var labelBounds = new List<Rect>();
        var labels = new List<Border>();
        var cardBounds = _positions.Values.Select(p => new Rect(p, new Size(CardWidth, CardHeight))).ToArray();
        // Use a canonical endpoint order so edges in opposite directions get distinct lanes too.
        foreach (var group in _relations.GroupBy(r => string.CompareOrdinal(r.FromId, r.ToId) < 0 ? (r.FromId, r.ToId) : (r.ToId, r.FromId)))
        {
            var relations = group.ToList();
            var canonicalFrom = Center(_positions[group.Key.Item1]);
            var canonicalTo = Center(_positions[group.Key.Item2]);
            var axis = canonicalTo - canonicalFrom;
            if (axis.Length < 1) axis = new Vector(1, 0);
            axis.Normalize(); var normal = new Vector(-axis.Y, axis.X);
            for (int i = 0; i < relations.Count; i++)
            {
                var relation = relations[i];
                var from = Center(_positions[relation.FromId]); var to = Center(_positions[relation.ToId]);
                double lane = (i - (relations.Count - 1) / 2.0) * 72;
                var control = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2) + normal * lane;
                // Coincident imported positions still yield a visible, finite relationship arc.
                if ((to - from).Length < 1) control = from + new Vector(110 + Math.Abs(lane), -110 - Math.Abs(lane));
                var start = Boundary(from, control - from);
                var end = Boundary(to, control - to);
                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = start, IsClosed = false };
                figure.Segments.Add(new QuadraticBezierSegment(control, end, true)); geometry.Figures.Add(figure);
                var color = SafeBrush(relation.Color, "#A2988B");
                _edges.Children.Add(new Path { Data = geometry, Stroke = color, StrokeThickness = 1.2, IsHitTestVisible = false });
                var tangent = end - control;
                if (tangent.Length < .001) tangent = new Vector(1, 0);
                tangent.Normalize(); var side = new Vector(-tangent.Y, tangent.X);
                _edges.Children.Add(new Polygon { Points = new PointCollection { end, end - tangent * 11 + side * 4.5, end - tangent * 11 - side * 4.5 }, Fill = color, IsHitTestVisible = false });
                var label = new Border
                {
                    Background = Brush("#FFFCF6"), BorderBrush = Brush("#DCD5C8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1),
                    Padding = new Thickness(8, 4, 8, 4), MaxWidth = 160, Cursor = Cursors.Hand,
                    ToolTip = string.IsNullOrWhiteSpace(relation.Note) ? $"{relation.Label} · 点击编辑关系" : relation.Label + "\n" + relation.Note,
                    Child = new TextBlock { Text = relation.Label, FontSize = 11, Foreground = Brush("#716A61"), TextTrimming = TextTrimming.CharacterEllipsis }
                };
                AutomationProperties.SetName(label, "关系：" + relation.Label);
                label.Measure(new Size(160, double.PositiveInfinity));
                var placement = PlaceLabel(start, control, end, label.DesiredSize, labelBounds, cardBounds);
                labelBounds.Add(placement.Bounds);
                Canvas.SetLeft(label, placement.Bounds.X); Canvas.SetTop(label, placement.Bounds.Y);
                if (placement.Displaced)
                {
                    // Dense graphs may require a nearby callout; preserve its attachment to the edge.
                    _edges.Children.Add(new Line { X1 = placement.Anchor.X, Y1 = placement.Anchor.Y, X2 = placement.Bounds.X + placement.Bounds.Width / 2, Y2 = placement.Bounds.Y + placement.Bounds.Height / 2, Stroke = color, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false });
                }
                label.MouseLeftButtonUp += (_, e) => { e.Handled = true; RelationSelected?.Invoke(relation); };
                labels.Add(label);
            }
        }
        // Keep all clickable labels above paths drawn later in the relationship list.
        foreach (var label in labels) _edges.Children.Add(label);
    }

    private static (Rect Bounds, Point Anchor, bool Displaced) PlaceLabel(Point start, Point control, Point end, Size size, List<Rect> placed, Rect[] cards)
    {
        double[] fractions = [.5, .35, .65, .25, .75, .15, .85];
        Rect At(Point center) => new(new Point(Math.Max(4, center.X - size.Width / 2), Math.Max(4, center.Y - size.Height / 2)), size);
        bool Free(Rect bounds)
        {
            bounds.Inflate(6, 6);
            return !placed.Any(bounds.IntersectsWith) && !cards.Any(bounds.IntersectsWith);
        }
        // Search on the curve first, so crossing pairs remain visually attached to their own edges.
        foreach (double fraction in fractions)
        {
            var anchor = CurvePoint(start, control, end, fraction);
            var bounds = At(anchor);
            if (Free(bounds)) return (bounds, anchor, false);
        }
        for (int ring = 1; ring <= 6; ring++)
        {
            foreach (double fraction in fractions)
            {
                var anchor = CurvePoint(start, control, end, fraction);
                var tangent = (control - start) * (1 - fraction) + (end - control) * fraction;
                if (tangent.Length < .001) tangent = new Vector(1, 0);
                tangent.Normalize(); var normal = new Vector(-tangent.Y, tangent.X);
                foreach (int sign in new[] { 1, -1 })
                {
                    var bounds = At(anchor + normal * (sign * ring * (size.Height + 12)));
                    if (Free(bounds)) return (bounds, anchor, true);
                }
            }
        }
        // A free strip below the graph guarantees that even heavily crowded labels remain usable.
        var midpoint = CurvePoint(start, control, end, .5);
        double bottom = cards.Select(r => r.Bottom).Concat(placed.Select(r => r.Bottom)).DefaultIfEmpty(0).Max();
        return (new Rect(Math.Max(4, midpoint.X - size.Width / 2), bottom + 24, size.Width, size.Height), midpoint, true);
    }

    private static Point CurvePoint(Point start, Point control, Point end, double fraction)
    {
        double remaining = 1 - fraction;
        return new Point(remaining * remaining * start.X + 2 * remaining * fraction * control.X + fraction * fraction * end.X,
            remaining * remaining * start.Y + 2 * remaining * fraction * control.Y + fraction * fraction * end.Y);
    }

    private void ResizeCanvas()
    {
        double right = _positions.Count == 0 ? 0 : _positions.Values.Max(p => p.X) + CardWidth + 110;
        double bottom = _positions.Count == 0 ? 0 : _positions.Values.Max(p => p.Y) + CardHeight + 110;
        foreach (var label in _edges.Children.OfType<Border>())
        {
            right = Math.Max(right, Canvas.GetLeft(label) + label.DesiredSize.Width + 24);
            bottom = Math.Max(bottom, Canvas.GetTop(label) + label.DesiredSize.Height + 24);
        }
        _canvas.Width = Math.Max(650, Math.Max(right, double.IsFinite(_scroll.ViewportWidth) ? _scroll.ViewportWidth : 0));
        _canvas.Height = Math.Max(500, Math.Max(bottom, double.IsFinite(_scroll.ViewportHeight) ? _scroll.ViewportHeight : 0));
        _edges.Width = _canvas.Width; _edges.Height = _canvas.Height;
    }

    private static double Coordinate(double value, double fallback) => Math.Clamp(double.IsFinite(value) ? value : fallback, 20, MaxCoordinate);
    private static Point Center(Point position) => position + new Vector(CardWidth / 2, CardHeight / 2);
    private static Point Boundary(Point center, Vector toward)
    {
        if (toward.Length < .001) toward = new Vector(1, 0);
        double scale = Math.Min(CardWidth / 2 / Math.Max(.001, Math.Abs(toward.X)), CardHeight / 2 / Math.Max(.001, Math.Abs(toward.Y)));
        return center + toward * scale;
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static Brush SafeBrush(string? color, string fallback)
    {
        try { return Brush(string.IsNullOrWhiteSpace(color) ? fallback : color); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return Brush(fallback); }
    }
}
