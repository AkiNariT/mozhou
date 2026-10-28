using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Mozhou.Core;
using Mozhou.Desktop;

internal static class Program
{
    private static int _checks;
    [STAThread] private static void Main()
    {
        var view = new GraphView();
        var project = new NovelProject();
        var canvas = (Canvas)((ScrollViewer)view.Content).Content;
        view.Render(project);
        Check(canvas.Children.OfType<StackPanel>().Count() == 1, "empty graph explains how to begin");
        var a = Person("A", 60, 60); var b = Person("B", 340, 160); var c = Person("C", 620, 80); var d = Person("D", 60, 300);
        project.Entries.AddRange([a, b, c, d]);
        project.Relations.AddRange([Edge(a,b,"信任"), Edge(b,a,"怀疑"), Edge(a,b,"同伴"), Edge(b,c,"认识")]);
        view.Render(project);
        Check(canvas.Children.OfType<Border>().Count() == 4, "all characters render");
        var edges = canvas.Children.OfType<Canvas>().Single();
        Check(edges.Children.OfType<System.Windows.Shapes.Path>().Count() == 4, "all directed edges render");
        Check(edges.Children.OfType<Polygon>().Count() == 4, "each directed edge has an arrow");
        var lanes = edges.Children.OfType<System.Windows.Shapes.Path>().Take(3).Select(p => ((QuadraticBezierSegment)((PathGeometry)p.Data).Figures[0].Segments[0]).Point1).Distinct().Count();
        Check(lanes == 3, "parallel and opposite relations use three distinct lanes");
        Check(edges.Children.OfType<Border>().Take(3).Select(x => Canvas.GetTop(x)).Distinct().Count() == 3, "parallel labels do not coincide");
        view.Render(project, a.Id);
        Check(canvas.Children.OfType<Border>().Count() == 2, "focus shows only person and immediate neighbors");
        Check(canvas.Children.OfType<Canvas>().Single().Children.OfType<Polygon>().Count() == 3, "focused graph omits unrelated edges");
        a.X = double.NaN; a.Y = double.PositiveInfinity; b.X = double.MaxValue; b.Y = -400;
        view.Render(project);
        Check(canvas.Children.OfType<Border>().All(card => double.IsFinite(Canvas.GetLeft(card)) && double.IsFinite(Canvas.GetTop(card)) && Canvas.GetLeft(card) >= 20 && Canvas.GetLeft(card) <= 12000 && Canvas.GetTop(card) >= 20), "invalid and runaway node coordinates are bounded");
        Check(double.IsFinite(canvas.Width) && double.IsFinite(canvas.Height) && canvas.Width >= 650 && canvas.Height >= 500 && canvas.Width < 13000, "canvas remains finite and usable");
        a.X = b.X = 80; a.Y = b.Y = 80;
        project.Relations[0].Color = "invalid-color";
        view.Render(project);
        Check(canvas.Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Path>().All(path => !path.Data.Bounds.IsEmpty && double.IsFinite(path.Data.Bounds.Width)), "coincident points and invalid edge colors render safely");
        var crossing = new NovelProject();
        var northWest = Person("林知夏", 80, 80); var northEast = Person("周砚", 400, 80);
        var southWest = Person("许青禾", 80, 300); var southEast = Person("陈望", 400, 300);
        crossing.Entries.AddRange([northWest, northEast, southWest, southEast]);
        crossing.Relations.AddRange([Edge(northWest, southEast, "交付"), Edge(southWest, northEast, "守约")]);
        view.Render(crossing);
        var crossingLabels = canvas.Children.OfType<Canvas>().Single().Children.OfType<Border>().ToArray();
        Rect LabelBounds(Border label) => new(Canvas.GetLeft(label), Canvas.GetTop(label), label.DesiredSize.Width, label.DesiredSize.Height);
        Check(!LabelBounds(crossingLabels[0]).IntersectsWith(LabelBounds(crossingLabels[1])), "crossing unrelated relations have disjoint clickable labels");
        Check(crossingLabels.All(label => !canvas.Children.OfType<Border>().Any(card => new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.Width, card.Height).IntersectsWith(LabelBounds(label)))), "crossing relation labels remain clear of character cards");
        var firstPlacement = crossingLabels.Select(LabelBounds).ToArray();
        view.Render(crossing);
        Check(canvas.Children.OfType<Canvas>().Single().Children.OfType<Border>().Select(LabelBounds).SequenceEqual(firstPlacement), "crossing label placement is stable across refreshes");
        foreach (var person in crossing.Entries) { person.X = 80; person.Y = 80; }
        crossing.Relations.Clear();
        for (int i = 0; i < 36; i++) crossing.Relations.Add(Edge(crossing.Entries[i % 4], crossing.Entries[(i + 1) % 4], "关系说明 " + i));
        view.Render(crossing);
        var crowdedEdges = canvas.Children.OfType<Canvas>().Single();
        var crowdedLabels = crowdedEdges.Children.OfType<Border>().Select(LabelBounds).ToArray();
        Check(crowdedLabels.SelectMany((bounds, index) => crowdedLabels.Skip(index + 1).Select(other => !bounds.IntersectsWith(other))).All(clear => clear), "dense graph keeps every relation label disjoint");
        Check(crowdedLabels.All(bounds => !canvas.Children.OfType<Border>().Any(card => new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.Width, card.Height).IntersectsWith(bounds))), "dense graph labels avoid coincident cards");
        Check(crowdedEdges.Children.OfType<Line>().Any(), "displaced dense graph labels retain edge attachment lines");
        var firstLabelIndex = crowdedEdges.Children.IndexOf(crowdedEdges.Children.OfType<Border>().First());
        Check(crowdedEdges.Children.Cast<UIElement>().Skip(firstLabelIndex).All(element => element is Border), "all labels render above every relationship path");
        Check(crowdedLabels.All(bounds => bounds.Right <= canvas.Width && bounds.Bottom <= canvas.Height), "canvas includes displaced labels");
        Console.WriteLine($"PASS: {_checks} focused graph checks");
    }
    private static NovelEntry Person(string title, double x, double y) => new() { Kind = "character", Title = title, X = x, Y = y };
    private static CharacterRelation Edge(NovelEntry from, NovelEntry to, string label) => new() { FromId = from.Id, ToId = to.Id, Label = label };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); _checks++; Console.WriteLine("PASS: " + message); }
}
