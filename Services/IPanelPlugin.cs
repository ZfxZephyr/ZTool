using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// What a plugin exposes to the palette: a view made of rows, a say in the
/// keyboard, its settings, and a footer to hang an in-panel control on. The
/// clipboard history is the first thing built against this; a plugin runtime
/// has to be able to lift the interface out unchanged.
/// </summary>
public interface IPanelPlugin : IDisposable
{
    /// <summary>How the meta row names this view.</summary>
    string Title { get; }

    /// <summary>True while the plugin is gathering its rows and they are not yet truthful.</summary>
    bool IsBusy { get; }

    /// <summary>
    /// True only for the first gathering, after a cold start. The host spends
    /// its loading animation here and nowhere else — a manual refresh keeps the
    /// old rows on screen and must not cover them.
    /// </summary>
    bool IsInitialBuild { get; }

    /// <summary>Raised when the rows change under the host's feet.</summary>
    event Action? Changed;

    /// <summary>Rows matching <paramref name="query"/>. Empty means nothing matched.</summary>
    IReadOnlyList<PanelRow> Rows(string query);

    /// <summary>
    /// Shot at after the host's own keys: the host runs its arrows/Enter/Esc
    /// first and only asks the plugin on anything left over. The active row
    /// comes along so a plugin key can act on the row you are looking at.
    /// </summary>
    bool TryHandleKey(PanelRow? active, Key key, KeyModifiers modifiers);

    /// <summary>
    /// Descriptive, not visual: a settings surface can render these without the
    /// plugin shipping a single control. The footer is the separate escape hatch
    /// for something the plugin wants on screen now.
    /// </summary>
    IReadOnlyList<SettingDescriptor> Settings { get; }

    /// <summary>Optional in-panel control the host parks in the meta row.</summary>
    Control? Footer { get; }

    void Start();

    void Stop();
}

public enum SettingKind
{
    Toggle,
    TextList,
}

/// <summary>One setting a plugin has, described well enough to be drawn by someone else.</summary>
public sealed record SettingDescriptor
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required SettingKind Kind { get; init; }

    public required Func<object?> Read { get; init; }

    public required Action<object?> Write { get; init; }
}
