using System;
using Avalonia.Media.Imaging;

namespace ZTool.Models;

/// <summary>
/// One row in the palette, whichever plugin put it there. The host only knows
/// this shape: a name to show, something to look at, and what happens when you
/// take it. Everything specific to a feature lives behind
/// <see cref="ZTool.Services.IPanelPlugin"/>.
/// </summary>
public sealed record PanelRow
{
    /// <summary>Stable across refreshes, so a live update can hold the selection.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Prepared at index time. Null means the row falls back to <see cref="Glyph"/>.</summary>
    public Bitmap? Icon { get; init; }

    /// <summary>The quiet letter that stands in when there is no icon.</summary>
    public string Glyph { get; init; } = "›";

    /// <summary>Small mark on the right edge — a pinned row, for instance.</summary>
    public string Marker { get; init; } = string.Empty;

    /// <summary>What taking this row does. False keeps the palette up — navigating somewhere is not leaving.</summary>
    public required Func<bool> Activate { get; init; }

    public bool HasIcon => Icon is not null;

    public bool HasMarker => Marker.Length > 0;
}
