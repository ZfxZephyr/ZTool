using System;
using System.Collections.Generic;
using System.Linq;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Fans one query out over every provider and returns a single ordered list.
/// Scored against the title first, then its pinyin one notch below, then the
/// subtitle at a discount — so a path match can surface a file but never
/// outrank the same name as an application.
/// </summary>
public sealed class SearchService
{
    private const int MaxResults = 7;

    private static readonly IReadOnlyList<SearchResult> Empty = Array.Empty<SearchResult>();

    private readonly IResultProvider[] _providers;

    public SearchService(params IResultProvider[] providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<SearchResult> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Empty;
        }

        var needle = query.Trim();
        var hits = new List<(SearchResult Item, double Score)>();

        foreach (var provider in _providers)
        {
            foreach (var item in provider.Candidates())
            {
                var score = Ranking.Score(item.Title, needle);
                if (score <= 0)
                {
                    score = Ranking.ScorePinyin(item.PinyinFull, needle);
                    if (score <= 0)
                    {
                        score = Ranking.ScorePinyin(item.PinyinInitials, needle);
                    }
                }

                if (score <= 0)
                {
                    score = Ranking.Score(item.Subtitle, needle) * 0.6;
                }

                if (score > 0)
                {
                    hits.Add((item, score));
                }
            }
        }

        if (hits.Count == 0)
        {
            return Empty;
        }

        hits.Sort(Compare);

        return hits
            .Take(MaxResults)
            .Select(h => h.Item)
            .ToList();
    }

    /// <summary>
    /// Total order, down to the path. Two files can share a title and a score,
    /// and List.Sort is unstable — without a last resort on the subtitle the
    /// same query would reshuffle between runs.
    /// </summary>
    private static int Compare((SearchResult Item, double Score) a, (SearchResult Item, double Score) b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        // Ties resolve on the shorter name, then alphabetically, so the list
        // never reorders between keystrokes that score the same.
        var byLength = a.Item.Title.Length.CompareTo(b.Item.Title.Length);
        if (byLength != 0)
        {
            return byLength;
        }

        var byTitle = string.Compare(a.Item.Title, b.Item.Title, StringComparison.OrdinalIgnoreCase);
        return byTitle != 0
            ? byTitle
            : string.Compare(a.Item.Subtitle, b.Item.Subtitle, StringComparison.OrdinalIgnoreCase);
    }
}
