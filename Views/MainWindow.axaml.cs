using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ZTool.Models;
using ZTool.ViewModels;

namespace ZTool.Views;

public partial class MainWindow : Window
{
    // The Signal mark is the only place the accent hue appears while idle.
    private static readonly IBrush IdleMark = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush ActiveMark = new SolidColorBrush(Color.Parse("#FF4D2E"));

    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Deactivated += OnDeactivated;

        // Tunnelled: the query box owns the caret keys, but navigation has to
        // reach us before it does.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        PlaceOnScreen();
        QueryBox.Focus();
    }

    /// <summary>Called when the hotkey brings an already-built window back.</summary>
    public void Summon()
    {
        if (_closed)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        // A minimize-all gesture can leave a shown palette iconified; a
        // summon must always end with the slab in front.
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        PlaceOnScreen();

        // A summon is a new intent; the last query is not worth keeping.
        if (!string.IsNullOrEmpty(QueryBox.Text))
        {
            QueryBox.Text = string.Empty;
            QueryBox.CaretIndex = 0;
        }

        ViewModel?.NotePasteTarget();
        ViewModel?.ClearNotice();
        QueryBox.Focus();
    }

    private void PlaceOnScreen()
    {
        // A launcher belongs in the upper third of the screen, not dead centre.
        if (Screens.Primary is { } screen)
        {
            var work = screen.WorkingArea;
            var bounds = PixelSize.FromSize(ClientSize, screen.Scaling);

            Position = new PixelPoint(
                work.X + (work.Width - bounds.Width) / 2,
                work.Y + (int)(work.Height * 0.22));
        }
    }

    /// <summary>Hide, do not exit — the process keeps the global hotkey alive.</summary>
    public void Dismiss()
    {
        // Launching the quit command closes this window mid-flight; the same
        // keypress then tries to dismiss it.
        if (_closed || !IsVisible)
        {
            return;
        }

        Hide();

        // Leaving the palette takes the query with it: dismiss means the next
        // summon starts empty, wherever the dismissal came from.
        if (!string.IsNullOrEmpty(QueryBox.Text))
        {
            QueryBox.Text = string.Empty;
            QueryBox.CaretIndex = 0;
        }

        ViewModel?.ClearNotice();
        ViewModel?.ResetView();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Out of sight the moment something else takes focus, like any palette.
        if (IsVisible)
        {
            Dismiss();
        }
    }

    private void OnQueryFocusChanged(object? sender, RoutedEventArgs e)
    {
        var focused = QueryBox.IsFocused;
        PromptMark.Foreground = focused ? ActiveMark : IdleMark;
    }

    private void OnSlabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Borderless window: the slab is its own titlebar, but the query and
        // the result rows still own their clicks.
        if (IsInside(e.Source, QueryBox) || IsInside(e.Source, ResultsList))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not PanelRow row)
        {
            return;
        }

        if (ViewModel is { } vm && vm.Activate(row))
        {
            Dismiss();
        }
    }

    private static bool IsInside(object? source, Visual root)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, root))
            {
                return true;
            }
        }

        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = ViewModel;

        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                // The query box owns the keyboard, so the view switch is a chord
                // rather than bare Tab. The 「剪贴板」 command is the other door.
                vm?.CycleView(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                return;

            case Key.Escape:
                // Esc clears a query first, then dismisses the tool.
                if (!string.IsNullOrEmpty(QueryBox.Text))
                {
                    QueryBox.Text = string.Empty;
                    QueryBox.CaretIndex = 0;
                }
                else
                {
                    Dismiss();
                }

                e.Handled = true;
                return;

            case Key.Down when vm?.HasResults == true:
                vm.MoveSelection(1);
                e.Handled = true;
                return;

            case Key.Up when vm?.HasResults == true:
                vm.MoveSelection(-1);
                e.Handled = true;
                return;

            case Key.Enter or Key.Return when vm?.HasResults == true:
                if (vm.ActivateSelected())
                {
                    Dismiss();
                }

                e.Handled = true;
                return;
        }

        // Anything left over belongs to the plugin that owns this view.
        if (vm?.TryHandleKey(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
        }
    }
}
