using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.MapMask;

[Serializable]
public sealed class MapMaskState
{
    public List<MapMaskSelectedLabelState> SelectedLabelItems { get; set; } = [];

    public List<string> HiddenMapPointKeys { get; set; } = [];
}

[Serializable]
public sealed class MapMaskSelectedLabelState
{
    public string Id { get; set; } = string.Empty;

    public List<string> LabelIds { get; set; } = [];

    public string ParentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IconUrl { get; set; } = string.Empty;

    public int PointCount { get; set; }
}

public static class MapMaskStateStorage
{
    private const string StateRelativePath = @"User/mapmask.json";
    private static readonly object Locker = new();
    private static MapMaskState? _state;

    public static MapMaskState Read()
    {
        lock (Locker)
        {
            _state ??= ReadFromFile();
            return _state;
        }
    }

    public static void Save(MapMaskState state)
    {
        lock (Locker)
        {
            _state = Normalize(state);
            WriteToFile(_state);
        }
    }

    public static void Clear()
    {
        Save(new MapMaskState());
    }

    private static MapMaskState ReadFromFile()
    {
        try
        {
            var filePath = Global.Absolute(StateRelativePath);
            if (!File.Exists(filePath))
            {
                return new MapMaskState();
            }

            var json = File.ReadAllText(filePath);
            return Normalize(JsonSerializer.Deserialize<MapMaskState>(json, ConfigService.JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            return new MapMaskState();
        }
    }

    private static MapMaskState Normalize(MapMaskState? state)
    {
        state ??= new MapMaskState();
        state.SelectedLabelItems ??= [];
        state.HiddenMapPointKeys ??= [];
        state.SelectedLabelItems = state.SelectedLabelItems.Where(x => x != null).ToList();

        foreach (var item in state.SelectedLabelItems)
        {
            item.Id ??= string.Empty;
            item.LabelIds ??= [];
            item.ParentId ??= string.Empty;
            item.Name ??= string.Empty;
            item.IconUrl ??= string.Empty;
        }

        return state;
    }

    private static void WriteToFile(MapMaskState state)
    {
        try
        {
            var filePath = Global.Absolute(StateRelativePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(state, ConfigService.JsonOptions));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
    }
}
