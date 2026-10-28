using System.Windows;
using System.Windows.Controls;
using Mozhou.Core;

namespace Mozhou.Desktop;

public partial class MainWindow
{
    private bool _graphRefreshing;

    private void RefreshGraph()
    {
        if (_section != "graph" || CharacterGraph == null || GraphFocus == null) return;
        string? previousId = (GraphFocus.SelectedItem as NovelEntry)?.Id;
        _graphRefreshing = true;
        try
        {
            var items = new List<object> { "全部人物" };
            items.AddRange(_project.Entries.Where(e => e.Kind == "character"));
            GraphFocus.ItemsSource = items;
            GraphFocus.SelectedItem = items.OfType<NovelEntry>().FirstOrDefault(e => e.Id == previousId) ?? items[0];
        }
        finally { _graphRefreshing = false; }
        CharacterGraph.Render(_project, (GraphFocus.SelectedItem as NovelEntry)?.Id);
    }

    private void GraphFocusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_graphRefreshing || _refreshing || _loading || _section != "graph") return;
        CharacterGraph.Render(_project, (GraphFocus.SelectedItem as NovelEntry)?.Id);
    }

    private void AddRelationClick(object sender, RoutedEventArgs e)
    {
        if (!Flush()) return;
        var people = _project.Entries.Where(entry => entry.Kind == "character").ToList();
        if (people.Count < 2)
        {
            MessageBox.Show(this, "先新建至少两个人物，再为他们建立关系。", "新增人物关系", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var source = GraphFocus.SelectedItem as NovelEntry ?? (_selected?.Kind == "character" ? _selected : null) ?? people[0];
        var relation = new CharacterRelation { FromId = source.Id, ToId = people.First(p => p.Id != source.Id).Id };
        if (!Dialogs.EditRelation(this, _project, relation, true, out _)) return;
        _project.Relations.Add(relation); MarkDirty(); Flush(); RefreshGraph();
    }

    private void EditRelation(CharacterRelation relation)
    {
        if (!_project.Relations.Contains(relation) || !Flush()) return;
        var draft = new CharacterRelation { Id = relation.Id, FromId = relation.FromId, ToId = relation.ToId, Label = relation.Label, Note = relation.Note, Color = relation.Color };
        if (!Dialogs.EditRelation(this, _project, draft, false, out bool remove)) return;
        if (remove) _project.Relations.Remove(relation);
        else
        {
            relation.FromId = draft.FromId; relation.ToId = draft.ToId; relation.Label = draft.Label; relation.Note = draft.Note; relation.Color = draft.Color;
        }
        MarkDirty(); Flush(); RefreshGraph();
    }

    private void ArrangeGraphClick(object sender, RoutedEventArgs e)
    {
        if (!Flush()) return;
        var people = _project.Entries.Where(entry => entry.Kind == "character").ToList();
        if (people.Count == 0) return;
        if (people.Count <= 8)
        {
            double radiusX = Math.Max(210, people.Count * 38), radiusY = Math.Max(155, people.Count * 25);
            for (int i = 0; i < people.Count; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI * 2 / people.Count;
                people[i].X = Math.Round(60 + radiusX + Math.Cos(angle) * radiusX);
                people[i].Y = Math.Round(60 + radiusY + Math.Sin(angle) * radiusY);
                people[i].UpdatedAt = DateTimeOffset.Now;
            }
        }
        else
        {
            int columns = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(people.Count * 1.35)));
            for (int i = 0; i < people.Count; i++)
            {
                people[i].X = 60 + i % columns * 250; people[i].Y = 60 + i / columns * 180; people[i].UpdatedAt = DateTimeOffset.Now;
            }
        }
        MarkDirty(); Flush(); RefreshGraph();
    }
}
