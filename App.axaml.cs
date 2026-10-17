using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ZTool.Services;
using ZTool.ViewModels;
using ZTool.Views;

namespace ZTool;

public partial class App : Application
{
    private GlobalHotkey? _hotkey;

    /// <summary>Ownership token claimed before Avalonia starts; see Program.</summary>
    public static SingleInstance? Instance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            viewModel.QuitRequested += window.Close;
            viewModel.Start();

            _hotkey = new GlobalHotkey();
            if (_hotkey.Register())
            {
                // The summon key toggles — but only against a palette that is
                // really front and centre. Any other state (iconified by a
                // minimize-all, covered without a deactivation) is a summon,
                // so one press always ends with the slab in front.
                _hotkey.Pressed += () =>
                {
                    if (window.IsVisible && window.IsActive)
                    {
                        window.Dismiss();
                    }
                    else
                    {
                        window.Summon();
                    }
                };
            }

            // Report either way — the footer must never claim a key we lost.
            viewModel.ReportHotkey(_hotkey.Bound, _hotkey.ShellFailure, _hotkey.FellBack);

            // A second launch of the exe knocks instead of stacking a copy.
            Instance?.OnSummonRequested(window.Summon);

            BuildTrayIcon(window);

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                viewModel.Shutdown();
                _hotkey?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The process outlives the window, so there has to be a visible place to
    /// summon or kill it. Without this, hiding the palette orphans the hotkey.
    /// </summary>
    private void BuildTrayIcon(MainWindow window)
    {
        var icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ZTool/Assets/avalonia-logo.ico"))),
            ToolTipText = "ZTool",
        };

        icon.Clicked += (_, _) => window.Summon();

        var show = new NativeMenuItem("显示 ZTool");
        show.Click += (_, _) => window.Summon();

        var quit = new NativeMenuItem("退出");
        quit.Click += (_, _) => window.Close();

        var menu = new NativeMenu();
        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        icon.Menu = menu;

        TrayIcon.SetIcons(this, new TrayIcons { icon });
    }
}
