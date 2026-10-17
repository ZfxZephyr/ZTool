using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZTool.Models;
using ZTool.Services;

namespace ZTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SearchPlugin _search;
    private readonly ClipboardPlugin _clipboard;
    private readonly IPanelPlugin[] _plugins;
    private int _active;

    private string _notice = string.Empty;

    // What the footer claims the summon chord is. Corrected as soon as the
    // shell reports what it actually managed to claim.
    private string _hotkeyModifier = "Alt";
    private string _hotkeyKey = "Space";
    private string _hotkeyNote = string.Empty;

    public MainViewModel()
    {
        _search = new SearchPlugin(() => QuitRequested?.Invoke());
        _search.NavigateRequested += () => Show(1);

        _clipboard = new ClipboardPlugin();
        _plugins = new IPanelPlugin[] { _search, _clipboard };

        foreach (var plugin in _plugins)
        {
            plugin.Changed += OnPluginChanged;
        }
    }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public ObservableCollection<PanelRow> Results { get; } = new();

    public bool HasResults => Results.Count > 0;

    public IPanelPlugin Active => _plugins[_active];

    public bool IsClipboard => ReferenceEquals(Active, _clipboard);

    public string ViewTitle => Active.Title;

    /// <summary>The in-panel control the active plugin parked, if it parked one.</summary>
    public Control? Footer => Active.Footer;

    public bool HasFooter => Footer is not null;

    /// <summary>The chord keycaps step aside when a plugin owns that corner.</summary>
    public bool HasKeycaps => HasHotkey && !HasFooter;

    /// <summary>True only for a cold start's first gathering — see the loading animation's one job.</summary>
    public bool IsIndexing => Active.IsBusy && Active.IsInitialBuild;

    /// <summary>
    /// History is a list you scan; a launcher is a list where the top row is the
    /// answer. The two views keep their own scale.
    /// </summary>
    public double ListMaxHeight => IsClipboard ? 720 : 420;

    /// <summary>
    /// Live readout under the query. The view's name leads it: the query box
    /// looks the same in both views, so the footer is the only place that says
    /// which one you are in.
    /// </summary>
    public string QueryStatus
    {
        get
        {
            if (_notice.Length > 0)
            {
                return _notice;
            }

            if (IsIndexing)
            {
                return "正在索引…";
            }

            var empty = string.IsNullOrWhiteSpace(Query);
            var count = Results.Count;

            string head;
            if (IsClipboard)
            {
                head = count == 0
                    ? empty ? "还没有复制记录" : "无匹配 · Esc 清空"
                    : empty
                        ? $"{count} 项 · ↑↓ 选择 · Enter 回贴 · Ctrl+P 固定"
                        : $"{count} 项匹配 · ↑↓ 选择 · Enter 回贴 · Ctrl+P 固定";
            }
            else
            {
                head = empty
                    ? "就绪"
                    : count == 0
                        ? "无匹配 · Esc 清空"
                        : $"{count} 项 · ↑↓ 选择 · Enter 打开";
            }

            var body = $"{ViewTitle} · {head}";
            return _hotkeyNote.Length > 0 ? $"{body} · {_hotkeyNote}" : body;
        }
    }

    /// <summary>The chord the keycaps in the footer should be showing.</summary>
    public string HotkeyModifier => _hotkeyModifier;

    public string HotkeyKey => _hotkeyKey;

    public bool HasHotkey { get; private set; } = true;

    /// <summary>
    /// Records what the shell really claimed. A launcher that silently loses
    /// its summon key is a launcher nobody can find again.
    /// </summary>
    public void ReportHotkey(HotkeyBinding? binding, string? shellFailure, bool fellBack)
    {
        if (binding is not null)
        {
            _hotkeyModifier = binding.Modifier;
            _hotkeyKey = binding.Key;
            HasHotkey = true;

            // The advertised chord is gone; say so before someone presses it.
            _hotkeyNote = fellBack ? $"Alt+Space 被占用 · 已改用 {binding.Modifier}+{binding.Key}" : string.Empty;
        }
        else
        {
            HasHotkey = false;
            _hotkeyNote = shellFailure is not null ? $"热键注册失败 · {shellFailure}" : "唤起热键均被占用 · 仅托盘可唤起";
        }

        OnPropertyChanged(nameof(HotkeyModifier));
        OnPropertyChanged(nameof(HotkeyKey));
        OnPropertyChanged(nameof(HasHotkey));
        OnPropertyChanged(nameof(HasKeycaps));
        OnPropertyChanged(nameof(QueryStatus));
    }

    public event Action? QuitRequested;

    public void Start()
    {
        foreach (var plugin in _plugins)
        {
            plugin.Start();
        }
    }

    public void Shutdown()
    {
        foreach (var plugin in _plugins)
        {
            plugin.Stop();
        }
    }

    /// <summary>Where a paste belongs. Called the moment the palette comes up.</summary>
    public void NotePasteTarget() => _clipboard.NotePasteTarget();

    /// <summary>A summon is a new intent: back to the empty search view.</summary>
    public void ResetView()
    {
        if (_active != 0)
        {
            Show(0);
        }
    }

    public void CycleView(int delta) => Show((_active + delta + _plugins.Length) % _plugins.Length);

    public void ClearNotice()
    {
        if (_notice.Length == 0)
        {
            return;
        }

        _notice = string.Empty;
        OnPropertyChanged(nameof(QueryStatus));
    }

    /// <summary>Opens the highlighted row. False means nothing was openable.</summary>
    public bool ActivateSelected()
    {
        return SelectedIndex >= 0 && SelectedIndex < Results.Count && Activate(Results[SelectedIndex]);
    }

    /// <summary>Opens one row. False means it refused, or asked to stay, and the palette remains.</summary>
    public bool Activate(PanelRow row)
    {
        try
        {
            return row.Activate();
        }
        catch (Exception ex)
        {
            _notice = $"打不开 · {ex.Message}";
            OnPropertyChanged(nameof(QueryStatus));
            return false;
        }
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = SelectedIndex + delta;
        SelectedIndex = next < 0 ? Results.Count - 1 : next >= Results.Count ? 0 : next;
    }

    /// <summary>
    /// Keys the palette does not own. Arrows, Enter and Esc have already had
    /// their shot by the time this is asked.
    /// </summary>
    public bool TryHandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        var active = SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        return Active.TryHandleKey(active, key, modifiers);
    }

    partial void OnQueryChanged(string value)
    {
        _notice = string.Empty;
        RefreshRows();
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0 && value < Results.Count)
        {
            OnPropertyChanged(nameof(QueryStatus));
        }
    }

    private void Show(int index)
    {
        _active = index;
        _notice = string.Empty;

        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(IsClipboard));
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(Footer));
        OnPropertyChanged(nameof(HasFooter));
        OnPropertyChanged(nameof(HasKeycaps));
        OnPropertyChanged(nameof(IsIndexing));
        OnPropertyChanged(nameof(ListMaxHeight));

        RefreshRows();
    }

    private void OnPluginChanged()
    {
        // The plugin may have moved a row under the selection. Come back to the
        // UI thread and hold onto the row the user was actually looking at.
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsIndexing));
            RefreshRows();
        });
    }

    private void RefreshRows()
    {
        var held = SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex].Id : null;

        var found = Active.Rows(Query);

        Results.Clear();
        foreach (var row in found)
        {
            Results.Add(row);
        }

        SelectedIndex = Results.Count == 0
            ? -1
            : held is null
                ? 0
                : Math.Max(0, IndexOf(held));

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(QueryStatus));
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (Results[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
