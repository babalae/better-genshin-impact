using System;
using System.ComponentModel;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BetterGenshinImpact.View.Windows;

public sealed class MapMiniFollowWindowController
{
    private readonly MapMiniFollowViewModel _viewModel;
    private MapMiniFollowWindow? _window;
    private bool _preserveVisibleStateOnClose;

    public MapMiniFollowWindowController(MapMiniFollowViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "ToggleMapMiniFollowWindow")
            {
                Toggle();
            }
        });

        if (_viewModel.IsVisible)
        {
            UIDispatcherHelper.BeginInvoke(Show);
        }
    }

    public void Toggle()
    {
        SetVisible(!_viewModel.IsVisible);
    }

    public void SetVisible(bool visible)
    {
        UIDispatcherHelper.BeginInvoke(() => _viewModel.IsVisible = visible);
    }

    public void Show()
    {
        if (_window is { IsVisible: true })
        {
            _viewModel.ReplayDisplaySnapshot();
            return;
        }

        _window = new MapMiniFollowWindow(_viewModel);
        _window.Closed += OnWindowClosed;
        _window.Show();
    }

    public void Hide(bool preserveVisibleState = false)
    {
        if (_window == null)
        {
            return;
        }

        _preserveVisibleStateOnClose = preserveVisibleState;
        try
        {
            _window.Close();
        }
        finally
        {
            _preserveVisibleStateOnClose = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MapMiniFollowViewModel.IsVisible))
        {
            return;
        }

        UIDispatcherHelper.BeginInvoke(() =>
        {
            if (_viewModel.IsVisible)
            {
                Show();
            }
            else
            {
                Hide();
            }
        });
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
        }

        _window = null;
        if (!_preserveVisibleStateOnClose && _viewModel.IsVisible)
        {
            _viewModel.IsVisible = false;
        }
    }
}
