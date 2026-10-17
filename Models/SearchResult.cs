using System;
using Avalonia.Media.Imaging;

namespace ZTool.Models;

/// <summary>
/// One thing the query can turn into. <see cref="Launch"/> carries the action
/// rather than a path so commands and files can share the same list.
/// </summary>
public sealed class SearchResult
{
    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public required ResultKind Kind { get; init; }

    /// <summary>What taking this row does. False keeps the palette up.</summary>
    public required Func<bool> Launch { get; init; }

    /// <summary>The app as the shell draws it. Null means the row shows a letter.</summary>
    public Bitmap? Icon { get; init; }

    /// <summary>Full pinyin of the title's Han runs, e.g. "jishiben".</summary>
    public string PinyinFull { get; init; } = string.Empty;

    /// <summary>First letters of those same runs, e.g. "jsb".</summary>
    public string PinyinInitials { get; init; } = string.Empty;

    public bool HasIcon => Icon is not null;

    /// <summary>
    /// One quiet letter per kind so the row reads at a glance. Context lives
    /// in <see cref="Subtitle"/>; this only has to be recognisable.
    /// </summary>
    public string KindMark => Kind switch
    {
        ResultKind.Application => "A",
        ResultKind.File => "F",
        _ => "›",
    };
}
