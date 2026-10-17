using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Documents and downloads — the places people actually go looking for a file
/// they already have. Deep, whole-drive indexing is a background daemon's job;
/// this stays shallow so the first query is ready immediately.
/// </summary>
public sealed class FileIndex : IResultProvider
{
    private const int MaxFiles = 3000;

    private List<SearchResult> _files = new();

    public int Count => _files.Count;

    public IEnumerable<SearchResult> Candidates() => _files;

    public void Rebuild()
    {
        var found = new List<SearchResult>();

        foreach (var root in Roots())
        {
            foreach (var file in AppIndex.Walk(root))
            {
                if (found.Count >= MaxFiles)
                {
                    _files = found;
                    return;
                }

                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.') || Naming.IsSystemFile(name))
                {
                    continue;
                }

                var captured = file;
                found.Add(new SearchResult
                {
                    Title = name,
                    Subtitle = DirectoryLabel(captured),
                    Kind = ResultKind.File,
                    PinyinFull = TitlePinyin.Full(name),
                    PinyinInitials = TitlePinyin.Initials(name),
                    Launch = () =>
                    {
                        ShellOpen(captured);
                        return true;
                    },
                });
            }
        }

        _files = found;
    }

    private static IEnumerable<string> Roots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        if (!string.IsNullOrEmpty(profile))
        {
            // Downloads has no SpecialFolder of its own, and the profile root
            // itself is left out — walking it drags in AppData noise nobody searches.
            candidates.Add(Path.Combine(profile, "Downloads"));
            candidates.Add(Path.Combine(profile, "Desktop"));
        }

        return candidates
            .Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string DirectoryLabel(string file)
    {
        var dir = Path.GetDirectoryName(file);
        return string.IsNullOrEmpty(dir) ? string.Empty : dir;
    }

    private static void ShellOpen(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
