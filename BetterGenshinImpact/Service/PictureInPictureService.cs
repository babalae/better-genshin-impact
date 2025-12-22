using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Windows;
using OpenCvSharp;
using System;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoSkip;

namespace BetterGenshinImpact.Service;

public static class PictureInPictureService
{
    private static PictureInPictureWindow? _window;
    private static bool _manualClosed;

    public static bool IsManuallyClosed => _manualClosed;

    public static void Update(Mat frame)
    {
        if (_manualClosed)
        {
            return;
        }

        Mat? copy = null;
        if (TaskContext.Instance().Config.AutoSkipConfig.PictureInPictureSourceType == nameof(PictureSourceType.TriggerDispatcher))
        {
            copy = frame.Clone();
        }
        UIDispatcherHelper.BeginInvoke(() =>
        {
            EnsureWindow();
            if (_window == null)
            {
                copy?.Dispose();
                return;
            }

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _window.SetFrame(copy);
        });
    }

    public static void Hide(bool resetManual = false)
    {
        UIDispatcherHelper.BeginInvoke(() =>
        {
            if (resetManual)
            {
                _manualClosed = false;
            }

            if (_window != null && _window.IsVisible)
            {
                _window.Hide();
            }
        });
    }

    public static void ResetManualClose()
    {
        _manualClosed = false;
    }

    private static void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        _window = new PictureInPictureWindow();
        _window.ClosedByUser += () =>
        {
            _manualClosed = true;
        };
        _window.Closed += (_, _) =>
        {
            _window = null;
        };
    }
}
