using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Files and folders. Everything is the source when it is running — it is the
/// background daemon this class used to pretend not to need — and a shallow
/// walk of Desktop, Documents and Downloads stands in when it is not, so a
/// missing dependency costs speed and not the feature.
///
/// Everything answers per query rather than handing over an index, so the rows
/// here are whatever the last query returned. Its own ordering is name
/// ascending because that is the one sort that makes the same query give the
/// same rows; the palette's ladder then decides what those rows are worth.
/// </summary>
public sealed class FileIndex : IResultProvider, IDisposable
{
    /// <summary>
    /// How many of Everything's matches to take. Wide enough that the row the
    /// ladder wants is almost always in the window, and Everything's latency is
    /// flat across this range — only the marshalling grows.
    /// </summary>
    private const int MaxCandidates = 2000;

    private const int MaxFiles = 3000;

    private EverythingClient? _everything;
    private List<SearchResult> _walked = new();
    private List<SearchResult> _rows = new();
    private string _requested = string.Empty;

    public FileIndex()
    {
        try
        {
            if (EverythingClient.IsRunning)
            {
                _everything = new EverythingClient();
                _everything.ResultsReady += OnEverythingAnswered;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or OutOfMemoryException)
        {
            // No reply window means no Everything. The walk is the fallback and
            // it does not need one.
            _everything = null;
        }
    }

    /// <summary>Raised when a query's rows arrive, so the palette can redraw.</summary>
    public event Action? Changed;

    public int Count => _rows.Count;

    public IEnumerable<SearchResult> Candidates() => _rows;

    /// <summary>Walks the usual folders. Only used when Everything is not answering.</summary>
    public void Rebuild()
    {
        var found = new List<SearchResult>();

        foreach (var root in Roots())
        {
            foreach (var file in AppIndex.Walk(root))
            {
                if (found.Count >= MaxFiles)
                {
                    _walked = found;
                    _rows = _everything is null ? _walked : _rows;
                    return;
                }

                var name = Path.GetFileName(file);
                if (!IsWanted(name))
                {
                    continue;
                }

                found.Add(Row(name, file));
            }
        }

        _walked = found;
        if (_everything is null)
        {
            _rows = _walked;
        }
    }

    /// <summary>
    /// Asks Everything for this query. Same query twice is one ask — the answer
    /// to the second would be the same rows and a redraw for nothing.
    /// </summary>
    public void Prime(string query)
    {
        if (_everything is null)
        {
            return;
        }

        // An empty query is not a file query. Everything would answer it with
        // the first rows of the whole index, which is noise the palette would
        // then spend a frame scoring.
        if (string.IsNullOrWhiteSpace(query))
        {
            _requested = string.Empty;
            _rows = [];
            return;
        }

        if (string.Equals(query, _requested, StringComparison.Ordinal))
        {
            return;
        }

        _requested = query;

        if (!_everything.Submit(query, MaxCandidates))
        {
            // Everything went away mid-session. Fall back rather than go blank.
            _everything = null;
            _rows = _walked;
            Changed?.Invoke();
        }
    }

    private void OnEverythingAnswered(string query, IReadOnlyList<EverythingHit> hits)
    {
        if (!string.Equals(query, _requested, StringComparison.Ordinal))
        {
            return;
        }

        var rows = new List<SearchResult>(hits.Count);

        foreach (var hit in hits)
        {
            var name = Path.GetFileName(hit.FullPath.TrimEnd('\\', '/'));
            if (IsWanted(name))
            {
                rows.Add(Row(name, hit.FullPath));
            }
        }

        _rows = rows;
        Changed?.Invoke();
    }

    private static SearchResult Row(string name, string path) => new()
    {
        Title = name,
        Subtitle = DirectoryLabel(path),
        Kind = ResultKind.File,
        PinyinFull = TitlePinyin.Full(name),
        PinyinInitials = TitlePinyin.Initials(name),
        Launch = () =>
        {
            ShellOpen(path);
            return true;
        },
    };

    private static bool IsWanted(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !Naming.IsSystemFile(name);

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

    public void Dispose()
    {
        _everything?.Dispose();
        _everything = null;
    }
}
