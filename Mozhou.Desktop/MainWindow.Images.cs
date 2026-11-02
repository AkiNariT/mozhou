using System.IO;
using System.Windows;
using Microsoft.Win32;
using Mozhou.Core;

namespace Mozhou.Desktop;

public partial class MainWindow
{
    private ImagePreviewWindow? _imagePreview;

    private void WireReferenceImages()
    {
        ReferenceImages.AddRequested += ChooseReferenceImages;
        ReferenceImages.PreviewRequested += PreviewReferenceImage;
        ReferenceImages.RemoveRequested += image =>
        {
            try { RemoveReferenceImage(image); }
            catch (Exception error) { ShowError("移除参考图失败", error); }
        };
    }

    private void RefreshReferenceImages() => ReferenceImages.SetImages(_store?.Folder, _selected?.Images ?? []);

    private void ClearReferenceImages()
    {
        CloseReferencePreview();
        ReferenceImages.SetImages(null, []);
    }

    private void CloseReferencePreview()
    {
        _imagePreview?.Close();
        _imagePreview = null;
    }

    private void ChooseReferenceImages()
    {
        if (_store == null || _selected == null) return;
        var dialog = new OpenFileDialog
        {
            Title = "添加参考图 · 图片将复制到小说文件夹",
            Filter = "图片（PNG、JPEG、BMP、GIF）|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try { AddReferenceImages(dialog.FileNames); }
        catch (Exception error) { ShowError("添加参考图失败", error); }
    }

    private bool AddReferenceImages(string[] paths)
    {
        if (_store == null || _selected == null || paths.Length == 0 || !Flush()) return false;
        // Validate the entire selection before changing any entry associations.
        foreach (var path in paths) LocalImageLoader.Validate(path);
        var storage = new ProjectImageStore(_store.Folder);
        var imported = new List<EntryImage>();
        foreach (var path in paths)
        {
            var image = storage.Import(path);
            // Validate the copied bytes too: the external source may have changed during import.
            LocalImageLoader.Validate(storage.ResolvePath(image));
            imported.Add(image);
        }
        _selected.Images.AddRange(imported);
        _selected.UpdatedAt = DateTimeOffset.Now;
        MarkDirty();
        RefreshReferenceImages();
        return Flush();
    }

    private bool RemoveReferenceImage(EntryImage image)
    {
        if (_store == null || _selected == null || !_selected.Images.Contains(image) || !Flush()) return false;
        _selected.Images.Remove(image);
        _selected.UpdatedAt = DateTimeOffset.Now;
        CloseReferencePreview();
        MarkDirty();
        RefreshReferenceImages();
        // Retain the managed file: previous saves may still reference it.
        return Flush();
    }

    private void PreviewReferenceImage(EntryImage image)
    {
        if (_store == null || _selected == null || !_selected.Images.Contains(image)) return;
        try
        {
            var path = new ProjectImageStore(_store.Folder).ResolvePath(image);
            var preview = new ImagePreviewWindow(this, path, image.Title);
            CloseReferencePreview();
            _imagePreview = preview;
            preview.Closed += (_, _) => { if (ReferenceEquals(_imagePreview, preview)) _imagePreview = null; };
            preview.Show();
        }
        catch (Exception error) { ShowError("无法查看参考图", error); }
    }
}
