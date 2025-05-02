using System;
using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Helpers;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// TODO 需要支持更多地图
/// </summary>
public partial class MapViewerViewModel : ObservableObject
{
    [ObservableProperty]
    private WriteableBitmap _mapBitmap;

    private readonly Mat _all256Map = new(Global.Absolute(@"Assets/Map/mainMap256Block.png"));

    private Mat _currentPathingMap = new(); // 2048级别

    private Rect _currentPathingRect = new(); // 2048级别

    public MapViewerViewModel()
    {
        var center = MapManager.GetMap(MapTypes.Teyvat).ConvertGenshinMapCoordinatesToImageCoordinates(0, 0);
        _mapBitmap = ClipMat(new Point2f((float)center.x, (float)center.y)).ToWriteableBitmap();
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "SendCurrentPosition")
            {
                UIDispatcherHelper.Invoke(() =>
                {
                    Debug.WriteLine("更新地图位置");
                    try
                    {
                        MapBitmap.Lock();
                        WriteableBitmapConverter.ToWriteableBitmap(ClipMat((Point2f)msg.NewValue), MapBitmap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    finally
                    {
                        MapBitmap.Unlock();
                    }
                });
            }
            else if (msg.PropertyName == "UpdateCurrentPathing")
            {
                Debug.WriteLine("更新当前追踪的路径图像");
                _currentPathingMap = GenTaskMat((PathingTask)msg.NewValue);
            }
        });
    }

    public Mat ClipMat(Point2f pos)
    {
        var len = 256;
        pos = new Point2f(pos.X - _currentPathingRect.X, pos.Y - _currentPathingRect.Y);
        Rect rect = new((int)pos.X - len, (int)pos.Y - len, len * 2, len * 2);
        // 实现剪切 Mat 的逻辑
        if (_currentPathingMap.Empty())
        {
            Debug.WriteLine("_currentPathingMap 未初始化");
            return new Mat(_all256Map, new Rect(rect.X / 8, rect.Y / 8, rect.Width, rect.Height));
        }
        else
        {
            Mat clipMat = new(_currentPathingMap, rect);
            clipMat = clipMat.Clone();
            // 绘制中心点
            Cv2.Circle(clipMat, new Point(len, len), 3, new Scalar(0, 255, 0), 2);
            return clipMat;
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="pathingTask"></param>
    /// <returns>2048级别的图像</returns>
    public Mat GenTaskMat(PathingTask pathingTask)
    {
        // 获取路径点位并转换为当前主要展示的地图点位 2048 级别
        var points = pathingTask.Positions;
        var mapPoints = points.Select(ConvertToMapPoint).ToList();
        var offsetRect = CalcRect(mapPoints);
        var offsetRect256 = new Rect(offsetRect.X / 8, offsetRect.Y / 8, offsetRect.Width / 8, offsetRect.Height / 8);

        // 把 _all256Map 的局部转化为 2048 级别
        Mat taskMat = new Mat(_all256Map, offsetRect256);
        taskMat = ResizeHelper.Resize(taskMat, 2048d / 256d);

        // 设置线条粗细
        int thickness = 2;

        // 绘制点位和连线
        for (int i = 0; i < mapPoints.Count - 1; i++)
        {
            var startPoint = mapPoints[i] - offsetRect.TopLeft;
            var endPoint = mapPoints[i + 1] - offsetRect.TopLeft;

            var lineColor = GetLineColor(points[i], points[i + 1]);
            var circleColor = GetCircleColor(points[i]);

            // 绘制点
            DrawCircle(taskMat, startPoint, circleColor, thickness);

            // 绘制线
            DrawLine(taskMat, startPoint, endPoint, lineColor, 1);
        }

        // 绘制最后一个点
        var lastPoint = mapPoints[^1] - offsetRect.TopLeft;
        var lastCircleColor = GetCircleColor(points[^1]);
        DrawCircle(taskMat, lastPoint, lastCircleColor, thickness);

        return taskMat;
    }

    private Rect CalcRect(List<Point> mapPoints)
    {
        // 计算其最大外接矩形
        _currentPathingRect = Cv2.BoundingRect(mapPoints);
        // 把矩形范围扩大一半
        _currentPathingRect.X -= 512;
        _currentPathingRect.Y -= 512;
        _currentPathingRect.Width += 1024;
        _currentPathingRect.Height += 1024;
        return _currentPathingRect;
    }

    private Point ConvertToMapPoint(Waypoint point)
    {
        var (x, y) = MapManager.GetMap(MapTypes.Teyvat).ConvertGenshinMapCoordinatesToImageCoordinates((float)point.X, (float)point.Y);
        return new Point(x, y);
    }

    private Scalar GetLineColor(Waypoint startPoint, Waypoint endPoint)
    {
        if (endPoint.Type == WaypointType.Path.Code || startPoint.Type == WaypointType.Teleport.Code)
        {
            return new Scalar(255, 0, 0); // 蓝色
        }
        else if (endPoint.Type == WaypointType.Target.Code)
        {
            return new Scalar(0, 0, 255); // 红色
        }

        return new Scalar(0, 0, 255); // 默认红色
    }

    private Scalar GetCircleColor(Waypoint point)
    {
        if (point.Type == WaypointType.Path.Code || point.Type == WaypointType.Teleport.Code)
        {
            return new Scalar(255, 0, 0); // 蓝色
        }
        else if (point.Type == WaypointType.Target.Code)
        {
            return new Scalar(0, 0, 255); // 红色
        }

        return new Scalar(0, 0, 255); // 默认红色
    }

    private void DrawCircle(Mat mat, Point point, Scalar color, int thickness)
    {
        Cv2.Circle(mat, point, 3, color, thickness);
    }

    private void DrawLine(Mat mat, Point startPoint, Point endPoint, Scalar color, int thickness)
    {
        Cv2.Line(mat, startPoint, endPoint, color, thickness);
    }
}