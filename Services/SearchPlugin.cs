using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// The launcher itself, expressed as a plugin: commands, applications and files
/// folded into one ordered list. Being a plugin is not decoration — it is the
/// seam the plugin runtime will eventually lift, so it carries the same
/// contract the clipboard does and nothing more.
/// </summary>
public sealed class SearchPlugin : IPanelPlugin
{
    private readonly AppIndex _apps = new();
    private readonly FileIndex _files = new();
    private readonly Action _quit;
    private readonly SearchService _search;
    private bool _initialBuild = true;
    private bool _busy;

    public SearchPlugin(Action quit)
    {
        _quit = quit;

        _search = new SearchService(
            new CommandProvider(
                ("退出 ZTool", "结束进程并注销唤起热键", () =>
                {
                    _quit();
                    return true;
                }),
                ("重新索引", "重扫开始菜单、系统应用与常用文件夹", () =>
                {
                    // Stays up: the footer says it is working, and that is the
                    // whole point of running it from here.
                    RebuildIndex();
                    return false;
                }),
                ("剪贴板", "打开剪贴板历史", () =>
                {
                    // A change of scene, not a departure.
                    NavigateRequested?.Invoke();
                    return false;
                })),
            _apps,
            _files);
    }

    public string Title => "搜索";

    public bool IsBusy => _busy;

    public bool IsInitialBuild => _initialBuild;

    public event Action? Changed;

    /// <summary>Raised when a row wants the palette to show a different plugin.</summary>
    public event Action? NavigateRequested;

    public IReadOnlyList<SettingDescriptor> Settings => Array.Empty<SettingDescriptor>();

    public Control? Footer => null;

    public IReadOnlyList<PanelRow> Rows(string query) =>
        _search.Search(query).Select(ToRow).ToList();

    public bool TryHandleKey(PanelRow? active, Key key, KeyModifiers modifiers) => false;

    /// <summary>Indexes a few thousand paths and a shelf of Store apps. Must not sit on the UI thread.</summary>
    public void Start() => Rebuild(indicateInitialBuild: true);

    public void Stop()
    {
        // The index is in-memory; there is nothing to wind down.
    }

    private void RebuildIndex() => Rebuild(indicateInitialBuild: false);

    private void Rebuild(bool indicateInitialBuild)
    {
        _busy = true;
        Changed?.Invoke();

        Task.Run(() =>
        {
            _apps.Rebuild();
            _files.Rebuild();

            _busy = false;
            _initialBuild = false;
            Changed?.Invoke();
        });
    }

    private static PanelRow ToRow(SearchResult item) => new()
    {
        // Search results are their own identity: two rows can share a title and
        // a subtitle and still be different things.
        Id = $"{item.Kind}:{item.Title}|{item.Subtitle}",
        Title = item.Title,
        Subtitle = item.Subtitle,
        Icon = item.Icon,
        Glyph = item.KindMark,
        Activate = item.Launch,
    };

    public void Dispose()
    {
    }
}
