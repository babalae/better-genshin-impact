using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

public class AutoLeyLineOutcropTask : ISoloTask
{
    private readonly ILogger<AutoLeyLineOutcropTask> _logger = App.GetLogger<AutoLeyLineOutcropTask>();
    private readonly AutoLeyLineOutcropParam _taskParam; 
    private readonly bool _oneDragonMode;
    private TpTask _tpTask = null!;
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private SwitchPartyTask? _switchPartyTask;
    private ISystemInfo _systemInfo = null!;

    private CancellationToken _ct;
    private AutoLeyLineConfigData? _configData;
    private NodeData? _nodeData;

    private double _leyLineX;
    private double _leyLineY;
    private int _currentRunTimes;
    private bool _marksStatus = true;
    private int _recheckCount;
    private int _consecutiveFailureCount;
    private DateTime _lastRewardNavLog = DateTime.MinValue;

    private RecognitionObject? _openRo;
    private RecognitionObject? _closeRo;
    private RecognitionObject? _paimonMenuRo;
    private RecognitionObject? _boxIconRo;
    private RecognitionObject? _mapSettingButtonRo;
    private RecognitionObject? _ocrRo1;
    private RecognitionObject? _ocrRo2;
    private RecognitionObject? _ocrRo3;
    private readonly RecognitionObject _ocrRoThis = RecognitionObject.OcrThis;

    private readonly Dictionary<string, Mat> _templateCache = new();

    private const int MaxRecheckCount = 3;
    private const int MaxConsecutiveFailures = 5;

    public string Name => "自动地脉花";

    public AutoLeyLineOutcropTask(AutoLeyLineOutcropParam taskParam, bool oneDragonMode = false)
    {
        _taskParam = taskParam;
        _oneDragonMode = oneDragonMode;
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;

        try
        {
            Initialize();
            var runTimesValue = await HandleResinExhaustionMode();
            if (runTimesValue <= 0)
            {
                throw new Exception("树脂耗尽，任务结束");
            }

            await PrepareForLeyLineRun();
            await RunLeyLineChallenges();

            if (_taskParam.IsResinExhaustionMode)
            {
                await RecheckResinAndContinue();
            }
        }
        catch (Exception e) when (e is NormalEndException or TaskCanceledException)
        {
            Logger.LogInformation("任务结束：{Msg}", e.Message);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "自动地脉花执行失败");
            _logger.LogError("自动地脉花执行失败:" + e.Message);
            if (_taskParam.IsNotification)
            {
                Notify.Event("AutoLeyLineOutcrop").Error($"任务失败: {e.Message}");
            }

            throw new Exception($"自动地脉花执行失败: {e.Message}", e);
        }
        finally
        {
            try
            {
                await EnsureExitRewardPage();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "地脉花结束后尝试退出奖励界面失败");
            }

            if (!_marksStatus)
            {
                await OpenCustomMarks();
            }
        }
    }

    private void Initialize()
    {
        _systemInfo = TaskContext.Instance().SystemInfo;
        _tpTask = new TpTask(_ct);
        ValidateSettings();
        LoadConfigData();
        LoadRecognitionObjects();
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_taskParam.LeyLineOutcropType))
        {
            throw new Exception("地脉花类型未选择");
        }

        if (_taskParam.LeyLineOutcropType != "启示之花" && _taskParam.LeyLineOutcropType != "藏金之花")
        {
            throw new Exception("地脉花类型无效，请重新选择");
        }

        if (string.IsNullOrWhiteSpace(_taskParam.Country))
        {
            throw new Exception("国家未配置");
        }

        if (!string.IsNullOrWhiteSpace(_taskParam.FriendshipTeam) && string.IsNullOrWhiteSpace(_taskParam.Team))
        {
            throw new Exception("配置好感队时必须配置战斗队伍");
        }

        if (_taskParam.Count < 1)
        {
            _taskParam.Count = 1;
        }
    }

    private void LoadConfigData()
    {
        // Load and validate the static ley line route config from disk.
        var workDir = Global.Absolute(@"GameTask\AutoLeyLineOutcrop");
        var configPath = Path.Combine(workDir, "Assets", "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("config.json 未找到", configPath);
        }

        var json = File.ReadAllText(configPath);
        _configData = JsonSerializer.Deserialize<AutoLeyLineConfigData>(json)
                      ?? throw new Exception("config.json 解析失败");
    }

    private void LoadRecognitionObjects()
    {
        // Template ROIs are tuned for the 1080p capture region.
        _openRo = BuildTemplate("Assets/icon/open.png");
        _closeRo = BuildTemplate("Assets/icon/close.png");
        _paimonMenuRo = BuildTemplate("Assets/icon/paimon_menu.png", new Rect(0, 0, ScaleTo1080(640), ScaleTo1080(216)));
        _boxIconRo = BuildTemplate("Assets/icon/box.png");
        _mapSettingButtonRo = BuildTemplate("Assets/icon/map_setting_button.bmp");

        _ocrRo1 = RecognitionObject.Ocr(ScaleTo1080(800), ScaleTo1080(200), ScaleTo1080(300), ScaleTo1080(100));
        _ocrRo2 = RecognitionObject.Ocr(ScaleTo1080(0), ScaleTo1080(200), ScaleTo1080(300), ScaleTo1080(300));
        _ocrRo3 = RecognitionObject.Ocr(ScaleTo1080(1200), ScaleTo1080(520), ScaleTo1080(300), ScaleTo1080(300));
    }

    private static int ScaleTo1080(int value)
    {
        // CaptureToRectArea returns a 1080p region already.
        return value;
    }

    private RecognitionObject BuildTemplate(string relativePath, Rect? roi = null, double threshold = 0.8)
    {
        // Cache + scale templates to the current asset scale to keep matching stable.
        var mat = LoadTemplate(relativePath);
        var ro = RecognitionObject.TemplateMatch(mat);
        ro.Threshold = threshold;
        if (roi.HasValue)
        {
            ro.RegionOfInterest = roi.Value;
        }

        return ro;
    }

    private Mat LoadTemplate(string relativePath)
    {
        if (_templateCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var workDir = Global.Absolute(@"GameTask\AutoLeyLineOutcrop");
        var fullPath = Path.Combine(workDir, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("模板素材未找到", fullPath);
        }

        var mat = Mat.FromStream(File.OpenRead(fullPath), ImreadModes.Color);
        // Resize once and reuse to avoid repeated scaling during recognition.
        var scaled = ResizeHelper.Resize(mat, _systemInfo.AssetScale);
        _templateCache[relativePath] = scaled;
        return scaled;
    }

    private async Task<int> HandleResinExhaustionMode()
    {
        if (!_taskParam.IsResinExhaustionMode)
        {
            return _taskParam.Count;
        }

        var result = await CalCountByResin();
        if (result.Count <= 0)
        {
            return 0;
        }

        if (_taskParam.OpenModeCountMin)
        {
            _taskParam.Count = Math.Min(result.Count, _taskParam.Count);
        }
        else
        {
            _taskParam.Count = result.Count;
        }

        if (_taskParam.IsNotification)
        {
            var text =
                "树脂耗尽模式统计结果:\n" +
                $"原粹树脂次数: {result.OriginalResinTimes}\n" +
                $"浓缩树脂次数: {result.CondensedResinTimes}\n" +
                $"须臾树脂次数: {result.TransientResinTimes}\n" +
                $"脆弱树脂次数: {result.FragileResinTimes}\n" +
                $"总次数: {result.Count}";
            Notify.Event("AutoLeyLineOutcrop").Send(text);
        }

        return _taskParam.Count;
    }

    private async Task PrepareForLeyLineRun()
    {
        await EnsureExitRewardPage();
        await _returnMainUiTask.Start(_ct);
        if (!_oneDragonMode)
        {
            await _tpTask.TpToStatueOfTheSeven();
        }

        if (!string.IsNullOrWhiteSpace(_taskParam.Team))
        {
            _switchPartyTask ??= new SwitchPartyTask();
            await _switchPartyTask.Start(_taskParam.Team, _ct);
        }

        if (_taskParam.UseAdventurerHandbook)
        {
            // The config flag means "do NOT use handbook"; close custom marks for manual navigation.
            await CloseCustomMarks();
        }

        TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);
    }

    private async Task RunLeyLineChallenges()
    {
        while (_currentRunTimes < _taskParam.Count)
        {
            if (!_taskParam.UseAdventurerHandbook)
            {
                // Handbook flow: open the book and track a ley line target.
                await FindLeyLineOutcropByBook(_taskParam.Country, _taskParam.LeyLineOutcropType);
            }
            else
            {
                // Manual flow: detect the ley line on the big map.
                await FindLeyLineOutcrop(_taskParam.Country, _taskParam.LeyLineOutcropType);
            }

            var foundStrategy = await ExecuteMatchingStrategy();
            if (!foundStrategy)
            {
                HandleNoStrategyFound();
                return;
            }
        }
    }

    private async Task<bool> ExecuteMatchingStrategy()
    {
        if (_configData?.LeyLinePositions == null)
        {
            throw new Exception("地脉花策略配置缺失");
        }

        if (!_configData.LeyLinePositions.TryGetValue(_taskParam.Country, out var positions))
        {
            return false;
        }

        foreach (var position in positions)
        {
            if (IsNearPosition(_leyLineX, _leyLineY, position.X, position.Y, _configData.ErrorThreshold))
            {
                _logger.LogInformation("匹配策略: {Strategy} order={Order}", position.Strategy, position.Order);
                await ExecutePathsUsingNodeData(position);
                return true;
            }
        }

        return false;
    }

    private static bool IsNearPosition(double x1, double y1, double x2, double y2, double threshold)
    {
        return Math.Abs(x1 - x2) <= threshold && Math.Abs(y1 - y2) <= threshold;
    }

    private async Task ExecutePathsUsingNodeData(LeyLinePosition position)
    {
        try
        {
            // Map node graph provides the walking routes for each ley line position.
            var nodeData = await LoadNodeData();
            var targetNode = FindTargetNodeByPosition(nodeData, position.X, position.Y);
            if (targetNode == null)
            {
                await EnsureExitRewardPage();
                return;
            }

            var paths = FindPathsToTarget(nodeData, targetNode);
            if (paths.Count == 0)
            {
                await EnsureExitRewardPage();
                return;
            }

            var optimal = SelectOptimalPath(paths);
            await ExecutePath(optimal);
            _currentRunTimes++;

            if (_currentRunTimes >= _taskParam.Count)
            {
                return;
            }

            var currentNode = targetNode;
            while (currentNode.Next.Count > 0 && _currentRunTimes < _taskParam.Count)
            {
                if (currentNode.Next.Count == 1)
                {
                    var next = currentNode.Next[0];
                    var nextNode = nodeData.Nodes.FirstOrDefault(n => n.Id == next.Target);
                    if (nextNode == null)
                    {
                        await EnsureExitRewardPage();
                        return;
                    }

                    var path = new PathInfo
                    {
                        StartNode = currentNode,
                        TargetNode = nextNode,
                        Routes = [next.Route]
                    };
                    await ExecutePath(path);
                    _currentRunTimes++;
                    currentNode = nextNode;
                }
                else
                {
                    // Multiple branches: re-locate the ley line position before deciding the route.
                    var originalX = _leyLineX;
                    var originalY = _leyLineY;

                    await _returnMainUiTask.Start(_ct);
                    await _tpTask.OpenBigMapUi();
                    var found = await LocateLeyLineOutcrop(_taskParam.LeyLineOutcropType);
                    await _returnMainUiTask.Start(_ct);

                    if (!found)
                    {
                        _leyLineX = originalX;
                        _leyLineY = originalY;
                        await EnsureExitRewardPage();
                        return;
                    }

                    var selected = SelectBranchRoute(nodeData, currentNode);
                    if (selected == null)
                    {
                        _leyLineX = originalX;
                        _leyLineY = originalY;
                        await EnsureExitRewardPage();
                        return;
                    }

                    var path = new PathInfo
                    {
                        StartNode = currentNode,
                        TargetNode = selected.Value.Node,
                        Routes = [selected.Value.Route]
                    };
                    await ExecutePath(path);
                    _currentRunTimes++;
                    currentNode = selected.Value.Node;
                }
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("战斗失败", StringComparison.OrdinalIgnoreCase))
            {
                _consecutiveFailureCount++;
                if (_consecutiveFailureCount >= MaxConsecutiveFailures)
                {
                    await EnsureExitRewardPage();
                    throw new Exception($"连续战斗失败{MaxConsecutiveFailures}次，任务终止");
                }

                await EnsureExitRewardPage();
                _logger.LogInformation("战斗失败，重新寻找地脉花");
                return;
            }

            await EnsureExitRewardPage();
            throw;
        }
    }

    private (string Route, Node Node)? SelectBranchRoute(NodeData nodeData, Node currentNode)
    {
        string? selectedRoute = null;
        Node? selectedNode = null;
        var closest = double.MaxValue;

        foreach (var next in currentNode.Next)
        {
            var branchNode = nodeData.Nodes.FirstOrDefault(n => n.Id == next.Target);
            if (branchNode == null)
            {
                continue;
            }

            var distance = Calculate2DDistance(_leyLineX, _leyLineY, branchNode.Position.X, branchNode.Position.Y);
            if (distance < closest)
            {
                closest = distance;
                selectedRoute = next.Route;
                selectedNode = branchNode;
            }
        }

        if (selectedRoute == null || selectedNode == null)
        {
            return null;
        }

        return (selectedRoute, selectedNode);
    }

    private static double Calculate2DDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private Node? FindTargetNodeByPosition(NodeData nodeData, double x, double y)
    {
        const double errorThreshold = 50;
        return nodeData.Nodes.FirstOrDefault(node =>
            node.Type == "blossom" &&
            Math.Abs(node.Position.X - x) <= errorThreshold &&
            Math.Abs(node.Position.Y - y) <= errorThreshold);
    }

    private List<PathInfo> FindPathsToTarget(NodeData nodeData, Node targetNode)
    {
        return BreadthFirstPathSearch(nodeData, targetNode);
    }

    private List<PathInfo> BreadthFirstPathSearch(NodeData nodeData, Node targetNode)
    {
        var validPaths = new List<PathInfo>();
        var teleportNodes = nodeData.Nodes.Where(n => n.Type == "teleport").ToList();
        var nodeMap = nodeData.Nodes.ToDictionary(n => n.Id, n => n);

        foreach (var startNode in teleportNodes)
        {
            var queue = new Queue<(Node Node, PathInfo Path, HashSet<int> Visited)>();
            // BFS ensures we prefer shorter paths from each teleport node.
            queue.Enqueue((startNode, new PathInfo
            {
                StartNode = startNode,
                TargetNode = targetNode,
                Routes = new List<string>()
            }, new HashSet<int> { startNode.Id }));

            while (queue.Count > 0)
            {
                var (current, path, visited) = queue.Dequeue();
                if (current.Id == targetNode.Id)
                {
                    validPaths.Add(path);
                    continue;
                }

                foreach (var next in current.Next)
                {
                    if (visited.Contains(next.Target))
                    {
                        continue;
                    }

                    if (!nodeMap.TryGetValue(next.Target, out var nextNode))
                    {
                        continue;
                    }

                    var newRoutes = new List<string>(path.Routes) { next.Route };
                    var newVisited = new HashSet<int>(visited) { next.Target };
                    queue.Enqueue((nextNode, new PathInfo
                    {
                        StartNode = path.StartNode,
                        TargetNode = targetNode,
                        Routes = newRoutes
                    }, newVisited));
                }
            }
        }

        validPaths.AddRange(FindReversePathsIfNeeded(nodeData, targetNode, validPaths));
        return validPaths;
    }

    private static List<PathInfo> FindReversePathsIfNeeded(NodeData nodeData, Node targetNode, List<PathInfo> existingPaths)
    {
        if (existingPaths.Count > 0 || targetNode.Prev.Count == 0)
        {
            return [];
        }

        // Fallback: allow a single hop into the target when no forward path exists.
        var reversePaths = new List<PathInfo>();
        var nodeMap = nodeData.Nodes.ToDictionary(n => n.Id, n => n);

        foreach (var prevNodeId in targetNode.Prev)
        {
            if (!nodeMap.TryGetValue(prevNodeId, out var prevNode))
            {
                continue;
            }

            var teleportNodes = nodeData.Nodes.Where(node =>
                node.Type == "teleport" && node.Next.Any(route => route.Target == prevNode.Id)).ToList();

            foreach (var teleportNode in teleportNodes)
            {
                var route = teleportNode.Next.FirstOrDefault(r => r.Target == prevNode.Id);
                var nextRoute = prevNode.Next.FirstOrDefault(r => r.Target == targetNode.Id);
                if (route == null || nextRoute == null)
                {
                    continue;
                }

                reversePaths.Add(new PathInfo
                {
                    StartNode = teleportNode,
                    TargetNode = targetNode,
                    Routes = [route.Route, nextRoute.Route]
                });
            }
        }

        return reversePaths;
    }

    private static PathInfo SelectOptimalPath(List<PathInfo> paths)
    {
        if (paths.Count == 0)
        {
            throw new Exception("没有可用路径");
        }

        return paths.OrderBy(p => p.Routes.Count).First();
    }

    private async Task ExecutePath(PathInfo path)
    {
        foreach (var routePath in path.Routes)
        {
            await RunPathingFile(routePath);
        }

        var lastRoute = path.Routes.Last();
        var targetRoute = lastRoute.Replace("Assets/pathing/", "Assets/pathing/target/").Replace("-rerun", "");
        await ProcessLeyLineOutcrop(_taskParam.Timeout, targetRoute);

        var rewardSuccess = await AttemptReward();
        if (!rewardSuccess)
        {
            throw new Exception("无法领取奖励");
        }

        _consecutiveFailureCount = 0;
    }

    private async Task RunPathingFile(string routePath)
    {
        await _returnMainUiTask.Start(_ct);

        var workDir = Global.Absolute(@"GameTask\AutoLeyLineOutcrop");
        var localPath = routePath.Replace("/", Path.DirectorySeparatorChar.ToString());
        var fullPath = Path.Combine(workDir, localPath);

        var task = PathingTask.BuildFromFilePath(fullPath) ?? throw new Exception("路径文件解析失败");
        var executor = new PathExecutor(_ct);
        await executor.Pathing(task);
    }

    private async Task<NodeData> LoadNodeData()
    {
        if (_nodeData != null)
        {
            return _nodeData;
        }

        var workDir = Global.Absolute(@"GameTask\AutoLeyLineOutcrop");
        var nodePath = Path.Combine(workDir, "Assets", "LeyLineOutcropData.json");
        if (!File.Exists(nodePath))
        {
            throw new FileNotFoundException("LeyLineOutcropData.json 未找到", nodePath);
        }

        var raw = JsonSerializer.Deserialize<RawNodeData>(File.ReadAllText(nodePath))
                  ?? throw new Exception("节点数据解析失败");
        _nodeData = AdaptNodeData(raw);
        return _nodeData;
    }

    private static NodeData AdaptNodeData(RawNodeData raw)
    {
        var nodes = new List<Node>();
        foreach (var teleport in raw.Teleports)
        {
            nodes.Add(new Node
            {
                Id = teleport.Id,
                Region = teleport.Region,
                Position = teleport.Position,
                Type = "teleport",
                Next = new List<NodeRoute>(),
                Prev = new List<int>()
            });
        }

        foreach (var blossom in raw.Blossoms)
        {
            nodes.Add(new Node
            {
                Id = blossom.Id,
                Region = blossom.Region,
                Position = blossom.Position,
                Type = "blossom",
                Next = new List<NodeRoute>(),
                Prev = new List<int>()
            });
        }

        foreach (var edge in raw.Edges)
        {
            var sourceNode = nodes.FirstOrDefault(n => n.Id == edge.Source);
            var targetNode = nodes.FirstOrDefault(n => n.Id == edge.Target);
            if (sourceNode == null || targetNode == null)
            {
                continue;
            }

            sourceNode.Next.Add(new NodeRoute { Target = edge.Target, Route = edge.Route });
            targetNode.Prev.Add(edge.Source);
        }

        return new NodeData
        {
            Nodes = nodes,
            Indexes = raw.Indexes
        };
    }

    private async Task FindLeyLineOutcrop(string country, string type)
    {
        if (_configData?.MapPositions == null)
        {
            throw new Exception("地图位置配置缺失");
        }

        if (!_configData.MapPositions.TryGetValue(country, out var positions) || positions.Count == 0)
        {
            throw new Exception($"未找到国家 {country} 的位置信息");
        }

        await _returnMainUiTask.Start(_ct);
        await _tpTask.OpenBigMapUi();

        await _tpTask.MoveMapTo(positions[0].X, positions[0].Y, MapTypes.Teyvat.ToString());
        var found = await LocateLeyLineOutcrop(type);
        if (found)
        {
            return;
        }

        for (var i = 1; i < positions.Count; i++)
        {
            var pos = positions[i];
            _logger.LogInformation("尝试定位地脉花: {Name}", pos.Name ?? $"{pos.X},{pos.Y}");
            await _tpTask.MoveMapTo(pos.X, pos.Y, MapTypes.Teyvat.ToString());
            if (await LocateLeyLineOutcrop(type))
            {
                return;
            }
        }

        await EnsureExitRewardPage();
        if (_taskParam.UseAdventurerHandbook)
        {
            _logger.LogWarning("寻找地脉花失败：当前已勾选“不使用冒险之证寻路”，可尝试关闭该选项后重试！");
            throw new Exception("寻找地脉花失败：未在地图上识别到地脉花图标。当前已勾选“不使用冒险之证寻路”，可尝试关闭该选项后重试！");
        }

        throw new Exception("寻找地脉花失败：未在地图上识别到地脉花图标");
    }

    private async Task<bool> LocateLeyLineOutcrop(string type)
    {
        await Delay(500, _ct);
        var currentZoom = _tpTask.GetBigMapZoomLevel(CaptureToRectArea());
        await _tpTask.AdjustMapZoomLevel(currentZoom, 3.0);

        var iconPath = type == "启示之花"
            ? "Assets/icon/Blossom_of_Revelation.png"
            : "Assets/icon/Blossom_of_Wealth.png";

        using var ra = CaptureToRectArea();
        var iconRo = BuildTemplate(iconPath);
        var list = ra.FindMulti(iconRo);
        if (list.Count == 0)
        {
            return false;
        }

        var flower = list[0];
        var center = _tpTask.GetBigMapCenterPoint(MapTypes.Teyvat.ToString());
        var mapZoomLevel = _tpTask.GetBigMapZoomLevel(CaptureToRectArea());
        var mapScaleFactor = TaskContext.Instance().Config.TpConfig.MapScaleFactor;
        _leyLineX = (960 - flower.X - 25) * mapZoomLevel / mapScaleFactor + center.X;
        _leyLineY = (540 - flower.Y - 25) * mapZoomLevel / mapScaleFactor + center.Y;
        return true;
    }

    private void HandleNoStrategyFound()
    {
        _logger.LogError("未找到对应的地脉花策略");
        if (_taskParam.IsNotification)
        {
            Notify.Event("AutoLeyLineOutcrop").Error("未找到对应的地脉花策略");
        }
    }

    private async Task<bool> ProcessLeyLineOutcrop(int timeoutSeconds, string targetPath, int retries = 0)
    {
        const int maxRetries = 3;
        if (retries >= maxRetries)
        {
            await EnsureExitRewardPage();
            throw new Exception("开启地脉花失败，已达最大重试次数");
        }

        await Delay(500, _ct);
        _logger.LogDebug("检测地脉花交互状态，重试次数: {Retries}/{MaxRetries}", retries + 1, maxRetries);
        using var capture = CaptureToRectArea();
        var result1 = FindSafe(capture, _ocrRo2!);
        var result2 = FindSafe(capture, _ocrRo3!);
        _logger.LogDebug("OCR结果: result1='{Text1}', result2='{Text2}'", result1.Text, result2.Text);

        if (result2.Text.Contains("之花", StringComparison.Ordinal))
        {
            _logger.LogDebug("识别到地脉之花入口");
            await SwitchToFriendshipTeamIfNeeded();
            return true;
        }

        if (result2.Text.Contains("溢口", StringComparison.Ordinal))
        {
            _logger.LogDebug("识别到溢口提示，尝试交互");
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            await Delay(300, _ct);
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            await Delay(500, _ct);
        }
        else if (!ContainsFightText(result1.Text))
        {
            _logger.LogDebug("未识别到战斗提示，执行路径: {Path}", targetPath);
            await RunPathingFile(targetPath);
            return await ProcessLeyLineOutcrop(timeoutSeconds, targetPath, retries + 1);
        }

        var fightResult = await AutoFight(timeoutSeconds);
        if (!fightResult)
        {
            await EnsureExitRewardPage();
            if (await ProcessResurrect())
            {
                return await ProcessLeyLineOutcrop(timeoutSeconds, targetPath, retries + 1);
            }

            throw new Exception("战斗失败");
        }

        await SwitchToFriendshipTeamIfNeeded();
        await AutoNavigateToReward();
        return true;
    }

    private Region FindSafe(ImageRegion capture, RecognitionObject ro)
    {
        var roi = ro.RegionOfInterest;
        if (roi == default)
        {
            return capture.Find(ro);
        }

        var clamped = roi.ClampTo(capture.Width, capture.Height);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return new Region();
        }

        if (clamped == roi)
        {
            return capture.Find(ro);
        }

        var cloned = ro.Clone();
        cloned.RegionOfInterest = clamped;
        return capture.Find(cloned);
    }

    private async Task<bool> AutoFight(int timeoutSeconds)
    {
        var fightCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        // Ley line uses OCR-based finish detection; disable auto-fight finish detect.
        var fightTask = StartAutoFightWithoutFinishDetect(fightCts.Token);
        var fightResult = await RecognizeTextInRegion(timeoutSeconds * 1000);
        fightCts.Cancel();

        try
        {
            await fightTask;
        }
        catch (Exception ex) when (ex is NormalEndException or TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "自动战斗任务结束");
        }
        finally
        {
            Simulation.ReleaseAllKey();
        }

        return fightResult;
    }

    private Task StartAutoFightWithoutFinishDetect(CancellationToken ct)
    {
        var autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
        var strategyPath = BuildAutoFightStrategyPath(autoFightConfig);
        var taskParam = new AutoFightParam(strategyPath, autoFightConfig)
        {
            FightFinishDetectEnabled = false,
            CheckBeforeBurst = false
        };
        // Avoid false finish signals for ley line fights.
        taskParam.FinishDetectConfig.FastCheckEnabled = false;
        taskParam.FinishDetectConfig.RotateFindEnemyEnabled = false;
        return new AutoFightTask(taskParam).Start(ct);
    }

    private static string BuildAutoFightStrategyPath(AutoFightConfig config)
    {
        var path = Global.Absolute(@"User\AutoFight\" + config.StrategyName + ".txt");
        if ("根据队伍自动选择".Equals(config.StrategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new Exception("战斗策略文件不存在");
        }

        return path;
    }

    private async Task<bool> RecognizeTextInRegion(int timeoutMs)
    {
        var start = DateTime.UtcNow;
        var noTextCount = 0;
        var successKeywords = new[] { "挑战达成", "战斗胜利", "挑战成功" };
        var failureKeywords = new[] { "挑战失败" };

        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            using var capture = CaptureToRectArea();
            var result = capture.Find(_ocrRo1!);
            var text = result.Text;

            if (successKeywords.Any(text.Contains))
            {
                // OCR recognizes victory text; treat as success.
                return true;
            }

            if (failureKeywords.Any(text.Contains))
            {
                // OCR recognizes failure text; stop early.
                return false;
            }

            var foundText = RecognizeFightText(capture);
            if (!foundText)
            {
                noTextCount++;
                if (noTextCount >= 10)
                {
                    return false;
                }
            }
            else
            {
                noTextCount = 0;
            }

            await Delay(1000, _ct);
        }

        return false;
    }

    private bool RecognizeFightText(ImageRegion captureRegion)
    {
        var result = captureRegion.Find(_ocrRo2!);
        var text = result.Text;
        return ContainsFightText(text);
    }

    private static bool ContainsFightText(string text)
    {
        var keywords = new[] { "打倒", "所有", "敌人" };
        return keywords.Any(text.Contains);
    }

    private async Task AutoNavigateToReward()
    {
        const int maxRetry = 3;
        for (var retry = 0; retry < maxRetry; retry++)
        {
            // Reset camera and move in short bursts to re-acquire the chest icon.
            _logger.LogInformation("开始导航到地脉花奖励，尝试 {Retry}/{Max}", retry + 1, maxRetry);
            Simulation.SendInput.Mouse.MiddleButtonClick();
            await Delay(300, _ct);

            if (await NavigateTowardReward(60000))
            {
                _logger.LogInformation("已到达领取奖励页面");
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_X);
            await Delay(500, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
            await Delay(1000, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            await Delay(500, _ct);
        }

        throw new Exception("导航到地脉花失败：超时未检测到奖励或交互文字");
    }

    private async Task<bool> NavigateTowardReward(int timeoutMs)
    {
        var start = DateTime.UtcNow;
        try
        {
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                // If reward UI is detected, stop moving.
                if (await DetectRewardPage())
                {
                    _logger.LogInformation("检测到奖励/交互文字，停止导航");
                    return true;
                }

                using var capture = CaptureToRectArea();
                if (_paimonMenuRo != null && capture.Find(_paimonMenuRo).IsEmpty())
                {
                    LogRewardNav("误入其他界面，尝试返回主界面");
                    await _returnMainUiTask.Start(_ct);
                }

                if (!AdjustViewForReward(capture))
                {
                    // Wait for the icon to re-enter view before moving forward.
                    LogRewardNav("未对正地脉花图标，等待重新定位");
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    await Delay(1000, _ct);
                    continue;
                }

                LogRewardNav("地脉花图标已对正，开始前进");
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                await Delay(200, _ct);
            }
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }

        return false;
    }

    private bool AdjustViewForReward(ImageRegion capture)
    {
        if (_boxIconRo == null)
        {
            return false;
        }

        // Use the chest icon position to align the camera before moving forward.
        var iconRes = capture.Find(_boxIconRo);
        if (iconRes.IsEmpty())
        {
            LogRewardNav("未找到地脉花图标");
            return false;
        }

        const int screenCenterX = 960;
        const int screenCenterY = 540;
        const double maxAngle = 10;

        var xOffset = iconRes.X - screenCenterX;
        var yOffset = screenCenterY - iconRes.Y;
        var angleInRadians = Math.Atan2(Math.Abs(xOffset), yOffset);
        var angleInDegrees = angleInRadians * (180 / Math.PI);
        var isAboveCenter = iconRes.Y < screenCenterY;
        var isWithinAngle = angleInDegrees <= maxAngle;

        if (isAboveCenter && isWithinAngle)
        {
            LogRewardNav("地脉花图标已对正，角度: {Angle}", angleInDegrees);
            return true;
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

        var moveX = Math.Clamp(xOffset, -300, 300);
        LogRewardNav("调整视角，xOffset={XOffset}, yOffset={YOffset}, angle={Angle}", xOffset, yOffset, angleInDegrees);
        Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);

        if (!isAboveCenter)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(0, 500);
        }

        return false;
    }

    private async Task<bool> DetectRewardPage()
    {
        using var capture = CaptureToRectArea();
        // Bv.FindF is faster for common keywords and avoids OCR misses.
        if (Bv.FindF(capture, "接触") || Bv.FindF(capture, "地脉") || Bv.FindF(capture, "之花"))
        {
            return true;
        }

        var list = capture.FindMulti(_ocrRoThis);
        foreach (var res in list)
        {
            if (res.Text.Contains("原粹树脂", StringComparison.Ordinal))
            {
                return true;
            }

            if (res.Text.Contains("接触", StringComparison.Ordinal)
                || res.Text.Contains("地脉", StringComparison.Ordinal)
                || res.Text.Contains("之花", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void LogRewardNav(string message, params object[] args)
    {
        var now = DateTime.UtcNow;
        // Throttle log spam during navigation loops.
        if ((now - _lastRewardNavLog).TotalSeconds < 3)
        {
            return;
        }

        _lastRewardNavLog = now;
        if (args.Length == 0)
        {
            _logger.LogInformation(message);
        }
        else
        {
            _logger.LogInformation(message, args);
        }
    }

    private async Task<bool> ProcessResurrect()
    {
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        foreach (var res in list)
        {
            if (res.Text.Contains("复苏", StringComparison.Ordinal))
            {
                res.Click();
                await Delay(2000, _ct);
                return true;
            }
        }

        return false;
    }

    private async Task SwitchToFriendshipTeamIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_taskParam.FriendshipTeam))
        {
            return;
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        try
        {
            _switchPartyTask ??= new SwitchPartyTask();
            await _switchPartyTask.Start(_taskParam.FriendshipTeam, _ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "切换好感队失败！");
        }
    }

    private async Task SwitchBackToCombatTeam()
    {
        if (string.IsNullOrWhiteSpace(_taskParam.Team))
        {
            return;
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        _switchPartyTask ??= new SwitchPartyTask();
        await _switchPartyTask.Start(_taskParam.Team, _ct);
    }

    private async Task<bool> AttemptReward(int retryCount = 0)
    {
        const int maxRetry = 3;
        if (retryCount >= maxRetry)
        {
            throw new Exception("领取奖励失败");
        }

        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
        await Delay(800, _ct);

        if (!await VerifyRewardPage())
        {
            await _returnMainUiTask.Start(_ct);
            await AutoNavigateToReward();
            return await AttemptReward(retryCount + 1);
        }

        var isOriginalResinEmpty = await CheckOriginalResinEmpty();
        var sortedButtons = FindAndSortUseButtons();
        if (sortedButtons.Count == 0)
        {
            await EnsureExitRewardPage();
            return false;
        }

        var resinChoice = await AnalyzeResinOptions(sortedButtons, isOriginalResinEmpty);
        if (resinChoice == null)
        {
            await EnsureExitRewardPage();
            return false;
        }

        resinChoice.Value.Click();
        await Delay(1000, _ct);

        if (!string.IsNullOrWhiteSpace(_taskParam.FriendshipTeam))
        {
            await SwitchBackToCombatTeam();
        }

        await Delay(1200, _ct);
        await EnsureExitRewardPage();
        return true;
    }

    private async Task<bool> VerifyRewardPage()
    {
        using var capture = CaptureToRectArea();
        var roi = new Rect(0, 0, capture.Width, capture.Height / 2);
        var list = capture.FindMulti(RecognitionObject.Ocr(roi));
        foreach (var res in list)
        {
            var text = res.Text;
            if (text.Contains("激活地脉之花", StringComparison.Ordinal) || text.Contains("选择激活方式", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> CheckOriginalResinEmpty()
    {
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        foreach (var res in list)
        {
            if (res.Text.Contains("补充", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private List<UseButton> FindAndSortUseButtons()
    {
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        var buttons = new List<UseButton>();

        foreach (var res in list)
        {
            var text = res.Text.Trim();
            if (text == "使用")
            {
                var centerX = res.X + res.Width / 2;
                var centerY = res.Y + res.Height / 2;
                buttons.Add(new UseButton(centerX, centerY, res.Y));
            }
        }

        return buttons.OrderBy(b => b.SortKey).ToList();
    }

    private async Task<UseButton?> AnalyzeResinOptions(List<UseButton> sortedButtons, bool isOriginalResinEmpty)
    {
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        var texts = list.Select(r => new { r.Text, r.Y }).ToList();

        var hasDoubleReward = texts.Any(t => t.Text.Contains("双倍", StringComparison.Ordinal) || t.Text.Contains("2倍产出", StringComparison.Ordinal) || t.Text.Contains("2倍", StringComparison.Ordinal));
        var hasOriginal20 = !isOriginalResinEmpty && texts.Any(t => t.Text.Contains("20", StringComparison.Ordinal) && t.Text.Contains("原粹", StringComparison.Ordinal));
        var hasOriginal40 = !isOriginalResinEmpty && texts.Any(t => t.Text.Contains("40", StringComparison.Ordinal) && t.Text.Contains("原粹", StringComparison.Ordinal));
        var hasCondensed = texts.Any(t => t.Text.Contains("浓缩", StringComparison.Ordinal));
        var hasTransient = texts.Any(t => t.Text.Contains("须臾", StringComparison.Ordinal));
        var hasFragile = texts.Any(t => t.Text.Contains("脆弱", StringComparison.Ordinal));

        if (isOriginalResinEmpty)
        {
            if (hasCondensed && sortedButtons.Count >= 1)
            {
                return sortedButtons[0];
            }

            if (hasTransient && _taskParam.UseTransientResin && sortedButtons.Count >= 1)
            {
                return sortedButtons[0];
            }

            if (hasFragile && _taskParam.UseFragileResin && sortedButtons.Count >= 1)
            {
                return sortedButtons[0];
            }

            return null;
        }

        if (hasDoubleReward && (hasOriginal20 || hasOriginal40))
        {
            if (hasOriginal20 && !hasOriginal40)
            {
                await TrySwitch20To40Resin();
            }

            return sortedButtons.FirstOrDefault();
        }

        if (hasCondensed && sortedButtons.Count >= 2)
        {
            return sortedButtons[1];
        }

        if (hasTransient && _taskParam.UseTransientResin && sortedButtons.Count >= 2)
        {
            return sortedButtons[1];
        }

        if (hasOriginal20 || hasOriginal40)
        {
            if (hasOriginal20 && !hasOriginal40)
            {
                await TrySwitch20To40Resin();
            }

            return sortedButtons.FirstOrDefault();
        }

        if (hasFragile && _taskParam.UseFragileResin && sortedButtons.Count >= 2)
        {
            return sortedButtons[1];
        }

        return sortedButtons.FirstOrDefault();
    }

    private async Task<bool> TrySwitch20To40Resin()
    {
        var switchRo = BuildTemplate("Assets/icon/switch_button.png", null, 0.7);
        using var capture = CaptureToRectArea();
        var res = capture.Find(switchRo);
        if (res.IsEmpty())
        {
            return false;
        }

        res.Click();
        await Delay(800, _ct);

        using var check = CaptureToRectArea();
        var list = check.FindMulti(_ocrRoThis);
        return list.Any(r => r.Text.Contains("40", StringComparison.Ordinal) && r.Text.Contains("原粹", StringComparison.Ordinal));
    }

    private async Task EnsureExitRewardPage()
    {
        const int maxAttempts = 5;
        for (var i = 0; i < maxAttempts; i++)
        {
            if (!await VerifyRewardPage())
            {
                return;
            }

            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(800, _ct);
        }
    }

    private async Task CloseCustomMarks()
    {
        await _returnMainUiTask.Start(_ct);
        Simulation.SendInput.SimulateAction(GIActions.OpenMap);
        await Delay(1000, _ct);
        GameCaptureRegion.GameRegion1080PPosClick(60, 1020);
        await Delay(600, _ct);

        using var capture = CaptureToRectArea();
        if (_openRo == null)
        {
            return;
        }

        var button = capture.Find(_openRo);
        if (button.IsExist())
        {
            _marksStatus = false;
            button.Click();
            await Delay(600, _ct);
        }

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }

    private async Task OpenCustomMarks()
    {
        await _returnMainUiTask.Start(_ct);
        Simulation.SendInput.SimulateAction(GIActions.OpenMap);
        await Delay(1000, _ct);
        GameCaptureRegion.GameRegion1080PPosClick(60, 1020);
        await Delay(600, _ct);

        if (_closeRo == null)
        {
            return;
        }

        using var capture = CaptureToRectArea();
        var buttons = capture.FindMulti(_closeRo);
        foreach (var button in buttons)
        {
            if (button.Y > ScaleTo1080(280) && button.Y < ScaleTo1080(350))
            {
                button.Click();
                _marksStatus = true;
                break;
            }
        }
    }

    private async Task FindLeyLineOutcropByBook(string country, string type)
    {
        await _returnMainUiTask.Start(_ct);
        await Delay(1000, _ct);

        Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook);
        await Delay(2500, _ct);

        GameCaptureRegion.GameRegion1080PPosClick(300, 550);
        await Delay(1000, _ct);
        GameCaptureRegion.GameRegion1080PPosClick(500, 200);
        await Delay(1000, _ct);
        GameCaptureRegion.GameRegion1080PPosClick(500, 500);
        await Delay(1000, _ct);

        if (type == "启示之花")
        {
            GameCaptureRegion.GameRegion1080PPosClick(700, 350);
        }
        else
        {
            GameCaptureRegion.GameRegion1080PPosClick(500, 350);
        }

        await Delay(1000, _ct);
        GameCaptureRegion.GameRegion1080PPosClick(1300, 800);
        await Delay(1000, _ct);

        await FindAndClickCountry(country);
        await FindAndCancelTrackingInBook();

        for (var retry = 0; retry < 3; retry++)
        {
            await Delay(1000, _ct);
            GameCaptureRegion.GameRegion1080PPosClick(1500, 850);
            await Delay(2500, _ct);

            if (await CheckBigMapOpened())
            {
                break;
            }

            if (retry < 2)
            {
                await _returnMainUiTask.Start(_ct);
                await FindAndClickCountry(country);
                await FindAndCancelTrackingInBook();
            }
            else
            {
                throw new Exception("大地图打开失败");
            }
        }

        var center = _tpTask.GetBigMapCenterPoint(MapTypes.Teyvat.ToString());
        _leyLineX = center.X;
        _leyLineY = center.Y;

        await CancelTrackingInMap();
    }

    private async Task<bool> CheckBigMapOpened()
    {
        if (_mapSettingButtonRo == null)
        {
            return false;
        }

        using var capture = CaptureToRectArea();
        return capture.Find(_mapSettingButtonRo).IsExist();
    }

    private async Task FindAndClickCountry(string country)
    {
        var match = country == "挪德卡莱" ? "挪德卡" : country;
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        var target = list.FirstOrDefault(r => r.Text.Contains(match, StringComparison.Ordinal));
        if (target == null)
        {
            throw new Exception($"冒险之证未找到国家: {country}");
        }

        target.Click();
    }

    private async Task FindAndCancelTrackingInBook()
    {
        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        var stop = list.FirstOrDefault(r => r.Text.Contains("停止", StringComparison.Ordinal));
        stop?.Click();
        await Delay(1000, _ct);
    }

    private async Task CancelTrackingInMap()
    {
        GameCaptureRegion.GameRegion1080PPosClick(960, 540);
        await Delay(1000, _ct);

        using var capture = CaptureToRectArea();
        var list = capture.FindMulti(_ocrRoThis);
        var stop = list.FirstOrDefault(r => r.Text.Contains("停止", StringComparison.Ordinal));
        if (stop != null)
        {
            stop.Click();
            return;
        }

        var leyLine = list.FirstOrDefault(r => r.Text.Contains("地脉", StringComparison.Ordinal) || r.Text.Contains("衍出", StringComparison.Ordinal));
        if (leyLine != null)
        {
            leyLine.Click();
            await Delay(1000, _ct);
            GameCaptureRegion.GameRegion1080PPosClick(1700, 1010);
            await Delay(1000, _ct);
        }
    }

    private async Task RecheckResinAndContinue()
    {
        _recheckCount++;
        if (_taskParam.OpenModeCountMin)
        {
            if (_currentRunTimes >= _taskParam.Count)
            {
                return;
            }
        }

        if (_recheckCount > MaxRecheckCount)
        {
            return;
        }

        var result = await CalCountByResin();
        if (result.Count <= 0)
        {
            return;
        }

        if (result.Count > 50)
        {
            return;
        }

        _currentRunTimes = 0;
        _taskParam.Count = result.Count;
        await RunLeyLineChallenges();
        await RecheckResinAndContinue();
    }

    private async Task<ResinCountResult> CalCountByResin()
    {
        var counts = await CountAllResin();

        var originalTimes = counts.OriginalResinCount / 40;
        var remaining = counts.OriginalResinCount % 40;
        if (remaining >= 20)
        {
            originalTimes += remaining / 20;
        }

        var condensedTimes = counts.CondensedResinCount;
        var transientTimes = _taskParam.UseTransientResin ? counts.TransientResinCount : 0;
        var fragileTimes = _taskParam.UseFragileResin ? counts.FragileResinCount : 0;

        return new ResinCountResult
        {
            Count = originalTimes + condensedTimes + transientTimes + fragileTimes,
            OriginalResinTimes = originalTimes,
            CondensedResinTimes = condensedTimes,
            TransientResinTimes = transientTimes,
            FragileResinTimes = fragileTimes
        };
    }

    private async Task<ResinCounts> CountAllResin()
    {
        await _returnMainUiTask.Start(_ct);
        await Delay(1500, _ct);

        Simulation.SendInput.SimulateAction(GIActions.OpenMap);
        await Delay(1500, _ct);

        var result = new ResinCounts
        {
            OriginalResinCount = await CountOriginalResin(),
            CondensedResinCount = await CountCondensedResin()
        };

        if (_taskParam.UseTransientResin || _taskParam.UseFragileResin)
        {
            await OpenReplenishResinUi();
            await Delay(1500, _ct);
            result.TransientResinCount = await CountTransientResin();
            result.FragileResinCount = await CountFragileResin();
        }

        await _returnMainUiTask.Start(_ct);
        return result;
    }

    private async Task<int> CountOriginalResin()
    {
        var icon = BuildTemplate("Assets/1920x1080/original_resin.png");
        using var capture = CaptureToRectArea();
        var res = capture.Find(icon);
        if (res.IsEmpty())
        {
            return 0;
        }

        var roi = new Rect(res.X, res.Y, ScaleTo1080(200), ScaleTo1080(40));
        using var region = capture.DeriveCrop(roi);
        var text = OcrFactory.Paddle.OcrWithoutDetector(region.CacheGreyMat);
        var match = Regex.Match(text, @"(\d{1,3})\s*/\s*\d+");
        if (match.Success)
        {
            return int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
        }

        return 0;
    }

    private async Task<int> CountCondensedResin()
    {
        var icon = BuildTemplate("Assets/1920x1080/condensed_resin.png");
        using var capture = CaptureToRectArea();
        var res = capture.Find(icon);
        if (res.IsEmpty())
        {
            return 0;
        }

        var roi = new Rect(res.Right, res.Y, ScaleTo1080(90), ScaleTo1080(40));
        using var region = capture.DeriveCrop(roi);
        var text = OcrFactory.Paddle.OcrWithoutDetector(region.CacheGreyMat);
        if (int.TryParse(Regex.Match(text, @"\d+").Value, out var value))
        {
            return value;
        }

        return await RecognizeWhiteNumber(region, capture);
    }

    private async Task<int> CountTransientResin()
    {
        var icon = BuildTemplate("Assets/1920x1080/transient_resin.png");
        using var capture = CaptureToRectArea();
        var res = capture.Find(icon);
        if (res.IsEmpty())
        {
            return 0;
        }

        var roi = new Rect(res.X, res.Bottom, res.Width, ScaleTo1080(60));
        using var region = capture.DeriveCrop(roi);
        return await RecognizeNumberWithFallback(region);
    }

    private async Task<int> CountFragileResin()
    {
        var icon = BuildTemplate("Assets/1920x1080/fragile_resin.png");
        using var capture = CaptureToRectArea();
        var res = capture.Find(icon);
        if (res.IsEmpty())
        {
            return 0;
        }

        var roi = new Rect(res.X, res.Bottom, res.Width, ScaleTo1080(60));
        using var region = capture.DeriveCrop(roi);
        return await RecognizeNumberWithFallback(region);
    }

    private async Task<int> RecognizeNumberWithFallback(ImageRegion region)
    {
        var text = OcrFactory.Paddle.OcrWithoutDetector(region.CacheGreyMat);
        if (int.TryParse(Regex.Match(text, @"\d+").Value, out var value))
        {
            return value;
        }

        return await RecognizeNumberByTemplate(region, false);
    }

    private async Task<int> RecognizeWhiteNumber(ImageRegion region, ImageRegion capture)
    {
        return await RecognizeNumberByTemplate(region, true);
    }

    private async Task<int> RecognizeNumberByTemplate(ImageRegion region, bool white)
    {
        var icons = white
            ? new Dictionary<int, string>
            {
                { 0, "Assets/1920x1080/num0_white.png" },
                { 1, "Assets/1920x1080/num1_white.png" },
                { 2, "Assets/1920x1080/num2_white.png" },
                { 3, "Assets/1920x1080/num3_white.png" },
                { 4, "Assets/1920x1080/num4_white.png" },
                { 5, "Assets/1920x1080/num5_white.png" }
            }
            : new Dictionary<int, string>
            {
                { 1, "Assets/1920x1080/num1.png" },
                { 2, "Assets/1920x1080/num2.png" },
                { 3, "Assets/1920x1080/num3.png" },
                { 4, "Assets/1920x1080/num4.png" }
            };

        foreach (var kvp in icons)
        {
            var ro = BuildTemplate(kvp.Value);
            var result = region.Find(ro);
            if (result.IsExist())
            {
                return kvp.Key;
            }
        }

        return 0;
    }

    private async Task OpenReplenishResinUi()
    {
        var ro = BuildTemplate("Assets/icon/replenish_resin_button.png");
        using var capture = CaptureToRectArea();
        var res = capture.Find(ro);
        if (res.IsExist())
        {
            res.Click();
        }
    }

    private class AutoLeyLineConfigData
    {
        [JsonPropertyName("errorThreshold")]
        public double ErrorThreshold { get; set; }

        [JsonPropertyName("mapPositions")]
        public Dictionary<string, List<MapPosition>> MapPositions { get; set; } = [];

        [JsonPropertyName("leyLinePositions")]
        public Dictionary<string, List<LeyLinePosition>> LeyLinePositions { get; set; } = [];
    }

    private class MapPosition
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class LeyLinePosition
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = string.Empty;

        [JsonPropertyName("steps")]
        public int Steps { get; set; }

        [JsonPropertyName("order")]
        public int Order { get; set; }
    }

    private class RawNodeData
    {
        [JsonPropertyName("teleports")]
        public List<RawNode> Teleports { get; set; } = [];

        [JsonPropertyName("blossoms")]
        public List<RawNode> Blossoms { get; set; } = [];

        [JsonPropertyName("edges")]
        public List<RawEdge> Edges { get; set; } = [];

        [JsonPropertyName("indexes")]
        public Dictionary<string, Dictionary<string, List<int>>> Indexes { get; set; } = [];
    }

    private class RawNode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("region")]
        public string Region { get; set; } = string.Empty;

        [JsonPropertyName("position")]
        public NodePosition Position { get; set; } = new();
    }

    private class RawEdge
    {
        [JsonPropertyName("source")]
        public int Source { get; set; }

        [JsonPropertyName("target")]
        public int Target { get; set; }

        [JsonPropertyName("route")]
        public string Route { get; set; } = string.Empty;
    }

    private class NodeData
    {
        public List<Node> Nodes { get; set; } = [];
        public Dictionary<string, Dictionary<string, List<int>>> Indexes { get; set; } = [];
    }

    private class Node
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
        public NodePosition Position { get; set; } = new();
        public string Type { get; set; } = string.Empty;
        public List<NodeRoute> Next { get; set; } = [];
        public List<int> Prev { get; set; } = [];
    }

    private class NodeRoute
    {
        public int Target { get; set; }
        public string Route { get; set; } = string.Empty;
    }

    private class NodePosition
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    private class PathInfo
    {
        public Node StartNode { get; set; } = new();
        public Node TargetNode { get; set; } = new();
        public List<string> Routes { get; set; } = [];
    }

    private class ResinCounts
    {
        public int OriginalResinCount { get; set; }
        public int CondensedResinCount { get; set; }
        public int TransientResinCount { get; set; }
        public int FragileResinCount { get; set; }
    }

    private class ResinCountResult
    {
        public int Count { get; set; }
        public int OriginalResinTimes { get; set; }
        public int CondensedResinTimes { get; set; }
        public int TransientResinTimes { get; set; }
        public int FragileResinTimes { get; set; }
    }

    private readonly struct UseButton
    {
        public int X { get; }
        public int Y { get; }
        public int SortKey { get; }

        public UseButton(int x, int y, int sortKey)
        {
            X = x;
            Y = y;
            SortKey = sortKey;
        }

        public void Click()
        {
            GameCaptureRegion.GameRegion1080PPosClick(X, Y);
        }
    }
}