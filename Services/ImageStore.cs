using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ZTool.Services;

/// <summary>
/// Screenshots on disk. One PNG per entry under the app folder, named by the
/// row's content hash so a save and its row can never drift apart.
/// </summary>
public static class ImageStore
{
    private const int ThumbnailEdge = 40;

    private static string Folder
    {
        get
        {
            var folder = Path.Combine(SettingsStore.Folder, "images");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string PathFor(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Writes the pixels and returns the file name it landed under.</summary>
    public static string Save(Bitmap bitmap, string contentHash)
    {
        var name = contentHash + ".png";

        using var stream = File.Create(PathFor(name));
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        return name;
    }

    public static Bitmap? Load(string fileName)
    {
        try
        {
            return File.Exists(PathFor(fileName)) ? new Bitmap(PathFor(fileName)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A small square stand-in for the list — enough to tell one screenshot from another.</summary>
    public static Bitmap? Thumbnail(string fileName)
    {
        using var full = Load(fileName);
        if (full is null)
        {
            return null;
        }

        try
        {
            var scaled = new RenderTargetBitmap(new PixelSize(ThumbnailEdge, ThumbnailEdge), new Vector(96, 96));
            using (var context = scaled.CreateDrawingContext())
            {
                var source = new Rect(0, 0, full.PixelSize.Width, full.PixelSize.Height);
                context.DrawImage(full, source, new Rect(0, 0, ThumbnailEdge, ThumbnailEdge));
            }

            return scaled;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            // A thumbnail that will not draw is a glyph row, not a missing entry.
            return null;
        }
    }

    public static void Delete(string fileName)
    {
        try
        {
            var path = PathFor(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An undeletable orphan is a disk-file problem, not a history problem.
        }
    }

    /// <summary>Removes image files no row references any more — eviction and deletes leave these behind.</summary>
    public static int Sweep(IEnumerable<string> referenced)
    {
        var keep = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(Folder, "*.png"))
        {
            var name = Path.GetFileName(file);
            if (keep.Contains(name))
            {
                continue;
            }

            Delete(name);
            removed++;
        }

        return removed;
    }
}
