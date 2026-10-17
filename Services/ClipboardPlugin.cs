using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Clipboard history as a panel plugin. It owns nothing of the palette's —
/// rows in, a couple of keys, one setting, one switch — which is exactly the
/// amount of surface a future plugin runtime has to be able to host.
/// </summary>
public sealed class ClipboardPlugin : IPanelPlugin
{
    /// <summary>The palette's "this row is held" mark. Quiet on purpose.</summary>
    private const string PinMark = "★";

    private const string RowPrefix = "clip:";

    private const string PauseKey = "clipboard.paused";

    private readonly ClipboardStore _store;
    private readonly ClipboardService _service;
    private readonly ToggleSwitch _pause;
    private readonly IReadOnlyList<SettingDescriptor> _settings;

    public ClipboardPlugin()
    {
        _store = new ClipboardStore();
        _service = new ClipboardService(_store);
        _service.Changed += () => Changed?.Invoke();

        // The descriptor is the contract; this switch is just the one place M1
        // puts it on screen. A settings surface would render the descriptor and
        // never see this control.
        _settings = new[]
        {
            new SettingDescriptor
            {
                Key = PauseKey,
                Label = "暂停记录",
                Kind = SettingKind.Toggle,
                Read = () => _service.IsPaused,
                Write = value => _service.IsPaused = value is true,
            },
        };

        _pause = new ToggleSwitch
        {
            Content = "记录",
            IsChecked = !_service.IsPaused,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _pause.IsCheckedChanged += (_, _) => _service.IsPaused = _pause.IsChecked != true;
    }

    public string Title => "剪贴板";

    public bool IsBusy => false;

    public bool IsInitialBuild => false;

    public event Action? Changed;

    public Control? Footer => _pause;

    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public IReadOnlyList<PanelRow> Rows(string query)
    {
        var rows = new List<PanelRow>();

        foreach (var entry in _store.Query(query, 12))
        {
            rows.Add(ToRow(entry));
        }

        return rows;
    }

    public bool TryHandleKey(PanelRow? active, Key key, KeyModifiers modifiers)
    {
        var entry = EntryOf(active);
        if (entry is null)
        {
            return false;
        }

        if (modifiers.HasFlag(KeyModifiers.Control) && key == Key.P)
        {
            _service.TogglePinned(entry);
            return true;
        }

        if (key == Key.Delete)
        {
            _service.Delete(entry);
            return true;
        }

        return false;
    }

    public void Start() => _service.Start();

    public void Stop() => _service.Dispose();

    /// <summary>Where the next paste belongs. The host calls this as the palette comes up.</summary>
    public void NotePasteTarget() => _service.NotePasteTarget();

    private ClipboardEntry? EntryOf(PanelRow? row)
    {
        if (row is null || !row.Id.StartsWith(RowPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return long.TryParse(row.Id[RowPrefix.Length..], out var id) ? _store.Find(id) : null;
    }

    private PanelRow ToRow(ClipboardEntry entry)
    {
        var image = entry.Kind == ClipboardKind.Image;

        return new PanelRow
        {
            Id = RowPrefix + entry.Id,
            Title = entry.Preview,
            Subtitle = image
                ? $"{entry.Preview} · {Relative(entry.LastUsedAt)} · {Blame(entry)}"
                : $"{Relative(entry.LastUsedAt)} · {Blame(entry)}",
            Icon = image ? ImageStore.Thumbnail(entry.Content) : null,
            Glyph = image ? "I" : "T",
            Marker = entry.Pinned ? PinMark : string.Empty,
            Activate = () =>
            {
                // Pasting is taking the row: the palette gets out of the way.
                _service.Paste(entry);
                return true;
            },
        };
    }

    private static string Blame(ClipboardEntry entry) =>
        string.IsNullOrEmpty(entry.SourceProcess) ? "未知来源" : entry.SourceProcess;

    /// <summary>Newness is what a history is sorted by, so it is what a row should say.</summary>
    private static string Relative(long unixMilliseconds)
    {
        var then = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        var span = DateTimeOffset.UtcNow - then;

        if (span.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} 分钟前";
        }

        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours} 小时前";
        }

        if (span.TotalDays < 2)
        {
            return "昨天";
        }

        return span.TotalDays < 7
            ? $"{(int)span.TotalDays} 天前"
            : then.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
