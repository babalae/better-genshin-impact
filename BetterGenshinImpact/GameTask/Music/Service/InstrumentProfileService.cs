using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Music.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class InstrumentProfileService : IInstrumentProfileService
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly ILogger<InstrumentProfileService> _logger = App.GetLogger<InstrumentProfileService>();
    private readonly string _profilePath = Global.Absolute(@"User\Music\instrument-profiles.json");

    private static readonly (char Key, int Note)[] StandardMappings =
    [
        ('Z', 48), ('X', 50), ('C', 52), ('V', 53), ('B', 55), ('N', 57), ('M', 59),
        ('A', 60), ('S', 62), ('D', 64), ('F', 65), ('G', 67), ('H', 69), ('J', 71),
        ('Q', 72), ('W', 74), ('E', 76), ('R', 77), ('T', 79), ('Y', 81), ('U', 83)
    ];

    private static readonly string[] BuiltInProfileNames =
    [
        "风物之诗琴", "老旧的诗琴", "镜花之琴", "谐律键琴", "跃律琴",
        "悠可琴", "晚风圆号", "余音", "绮筵之鼓", "聚聚鼓"
    ];

    public InstrumentProfileService()
    {
        Profiles = Load();
        EnsureBuiltInProfiles();
        StandardProfile = Profiles.First(x => x.Name == "风物之诗琴");
    }

    public ObservableCollection<InstrumentProfile> Profiles { get; }

    public InstrumentProfile StandardProfile { get; }

    public InstrumentProfile Find(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var candidate in name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var profile = Profiles.FirstOrDefault(x => x.Matches(candidate));
                if (profile != null)
                {
                    return profile;
                }
            }
        }

        return StandardProfile;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
            var json = JsonConvert.SerializeObject(Profiles, Formatting.Indented);
            File.WriteAllText(_profilePath, json, Utf8WithoutBom);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存乐器档案失败");
        }
    }

    private ObservableCollection<InstrumentProfile> Load()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = File.ReadAllText(_profilePath, Utf8WithoutBom);
                var profiles = JsonConvert.DeserializeObject<List<InstrumentProfile>>(json);
                if (profiles is { Count: > 0 })
                {
                    foreach (var profile in profiles)
                    {
                        profile.Aliases ??= [];
                        profile.Mappings ??= [];
                    }

                    return new ObservableCollection<InstrumentProfile>(profiles);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "读取乐器档案失败，将使用默认档案");
        }

        return [];
    }

    private void EnsureBuiltInProfiles()
    {
        var changed = false;
        foreach (var name in BuiltInProfileNames)
        {
            if (Profiles.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var mode = name is "绮筵之鼓" or "聚聚鼓"
                ? InstrumentMappingMode.Exact
                : InstrumentMappingMode.MelodicOctaveFold;
            Profiles.Add(CreateProfile(name, mode));
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    private static InstrumentProfile CreateProfile(string name, InstrumentMappingMode mode)
    {
        return new InstrumentProfile
        {
            Name = name,
            MappingMode = mode,
            Aliases = new ObservableCollection<string> { name },
            Mappings = new ObservableCollection<InstrumentKeyMapping>(
                StandardMappings.Select(x => new InstrumentKeyMapping(x.Key, x.Note)))
        };
    }
}
