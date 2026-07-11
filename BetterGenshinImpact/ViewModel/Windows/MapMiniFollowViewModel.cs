using System;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class MapMiniFollowViewModel : ObservableObject
{
    private const double MinFollowZoom = 0.2;
    private const double MaxFollowZoom = 8.0;
    private const double FollowZoomStep = 0.5;

    [ObservableProperty]
    private bool _isVisible = TaskContext.Instance().Config.DevConfig.MapMiniFollowVisible;

    [ObservableProperty]
    private bool _isTopmost = TaskContext.Instance().Config.DevConfig.MapMiniFollowTopmost;

    [ObservableProperty]
    private string _mapName = ResolveInitialMapName();

    [ObservableProperty]
    private string _mapDisplayName = GetMapDisplayName(ResolveInitialMapName());

    [ObservableProperty]
    private bool _isRecorderMode;

    [ObservableProperty]
    private bool _isPathRecorderRecording = PathRecorder.Instance.IsRecording;

    [ObservableProperty]
    private bool _showTeleportPoints = true;

    [ObservableProperty]
    private double _followZoom = 5.0;

    public string TitleText => $"{(IsRecorderMode ? "录制" : "调试")} / {MapDisplayName}";

    public string HotkeyText => FormatHotkeyText(TaskContext.Instance().Config.HotKeyConfig.MapMiniFollowWindowHotkey);

    public double IncreaseFollowZoom() => AdjustFollowZoom(FollowZoomStep);

    public double DecreaseFollowZoom() => AdjustFollowZoom(-FollowZoomStep);

    public MapMiniFollowViewModel()
    {
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "SendCurrentPosition" &&
                TryGetTrackedPosition(msg.NewValue, out var ignoredPoint, out var positionMapName))
            {
                UpdateMapName(positionMapName);
            }
            else if (msg.PropertyName == "SetMapViewerRecorderMode" && msg.NewValue is bool recorderMode)
            {
                IsRecorderMode = recorderMode;
                IsPathRecorderRecording = recorderMode && PathRecorder.Instance.IsRecording;
            }
            else if (msg.PropertyName == "UpdateRecorderPathing" && msg.NewValue is PathingTask recorderTask)
            {
                IsRecorderMode = true;
                IsPathRecorderRecording = PathRecorder.Instance.IsRecording;
                UpdateMapName(recorderTask.Info?.MapName);
            }
            else if (msg.PropertyName == "UpdateCurrentPathing" && msg.NewValue is PathingTask currentTask)
            {
                IsRecorderMode = false;
                IsPathRecorderRecording = false;
                UpdateMapName(currentTask.Info?.MapName);
            }
            else if (msg.PropertyName == "SetMapMiniFollowZoom" && msg.NewValue is double followZoom)
            {
                FollowZoom = followZoom;
            }
            else if (msg.PropertyName == "SetMapMiniFollowShowTeleportPoints" && msg.NewValue is bool showTeleportPoints)
            {
                ShowTeleportPoints = showTeleportPoints;
            }
        });
    }

    public void ReplayDisplaySnapshot()
    {
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "SetMapViewerRecorderMode",
            new object(),
            IsRecorderMode));

        var recorderTask = PathRecorder.Instance.CurrentTask;
        if (IsRecorderMode && recorderTask.Positions.Count > 0)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "UpdateRecorderPathing",
                new object(),
                recorderTask));
            return;
        }

        var currentTask = MapMaskRouteOverlayState.Get();
        if (currentTask?.Positions is { Count: > 0 })
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
                this,
                "UpdateCurrentPathing",
                new object(),
                currentTask));
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(
            this,
            "RequestMapDisplaySnapshot",
            new object(),
            new object()));
    }

    partial void OnIsVisibleChanged(bool value)
    {
        TaskContext.Instance().Config.DevConfig.MapMiniFollowVisible = value;
    }

    partial void OnIsTopmostChanged(bool value)
    {
        TaskContext.Instance().Config.DevConfig.MapMiniFollowTopmost = value;
    }

    partial void OnMapNameChanged(string value)
    {
        MapDisplayName = GetMapDisplayName(value);
    }

    partial void OnMapDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(TitleText));
    }

    partial void OnIsRecorderModeChanged(bool value)
    {
        OnPropertyChanged(nameof(TitleText));
    }

    private double AdjustFollowZoom(double delta)
    {
        var current = double.IsNaN(FollowZoom) || double.IsInfinity(FollowZoom)
            ? 5.0
            : FollowZoom;
        FollowZoom = Math.Round(Math.Clamp(current + delta, MinFollowZoom, MaxFollowZoom), 1, MidpointRounding.AwayFromZero);
        return FollowZoom;
    }

    private void UpdateMapName(string? mapName)
    {
        if (!TryNormalizeMapName(mapName, out var normalizedMapName) ||
            string.Equals(normalizedMapName, MapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MapName = normalizedMapName;
    }

    private static string ResolveInitialMapName()
    {
        return TryNormalizeMapName(TaskContext.Instance().Config.DevConfig.RecordMapName, out var mapName)
            ? mapName
            : nameof(MapTypes.Teyvat);
    }

    private static bool TryNormalizeMapName(string? mapName, out string normalizedMapName)
    {
        if (Enum.TryParse<MapTypes>(mapName, true, out var mapType))
        {
            normalizedMapName = mapType.ToString();
            return true;
        }

        normalizedMapName = string.Empty;
        return false;
    }

    private static string GetMapDisplayName(string mapName)
    {
        try
        {
            return MapTypesExtensions.ParseFromName(mapName).GetDescription();
        }
        catch
        {
            return string.IsNullOrWhiteSpace(mapName) ? nameof(MapTypes.Teyvat) : mapName;
        }
    }

    private static string FormatHotkeyText(string? hotkey)
    {
        return string.IsNullOrWhiteSpace(hotkey) ? "未绑定" : hotkey;
    }

    private static bool TryGetTrackedPosition(object? value, out Point2f point, out string? mapName)
    {
        switch (value)
        {
            case TrackedMapPosition tracked:
                point = tracked.ImagePosition;
                mapName = tracked.MapName;
                return true;
            case Point2f legacyPoint:
                point = legacyPoint;
                mapName = null;
                return true;
            default:
                point = default;
                mapName = null;
                return false;
        }
    }
}
