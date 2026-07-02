using BetterGenshinImpact.Core.Profile;
using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 管理 User/Profiles 下的通用 Profile 索引及目录结构。
/// 当前只创建云原神 Profile，但目录和基础模型不与云模块绑定，
/// 为未来 BetterGI 级多实例配置预留扩展位置。
/// </summary>
public sealed class ProfileService
{
    // 保护 Profile 索引和模块文件的进程内并发读写。
    private readonly object _syncRoot = new();

    // 通用 Profile 根目录。
    private readonly string _profilesRoot = Global.Absolute(@"User\Profiles");

    // 全部 Profile 基础信息的索引文件。
    private readonly string _indexPath = Global.Absolute(@"User\Profiles\profiles.json");

    /// <summary>
    /// 读取通用 Profile 索引；文件不存在或损坏时返回空集合。
    /// </summary>
    public IReadOnlyList<BetterGiProfile> ReadProfiles()
    {
        lock (_syncRoot)
        {
            EnsureRoot();
            if (!File.Exists(_indexPath))
            {
                // 首次使用尚未创建索引属于正常状态。
                return [];
            }

            try
            {
                return JsonConvert.DeserializeObject<List<BetterGiProfile>>(File.ReadAllText(_indexPath))
                       ?? [];
            }
            catch
            {
                // Profile 索引损坏不应阻止程序启动，交由用户后续重新创建实例。
                return [];
            }
        }
    }

    /// <summary>
    /// 创建一个云原神 Profile，并初始化独立 WebView2Data 和 Modules 目录。
    /// </summary>
    public BetterGiProfile CreateCloudProfile(string? name = null)
    {
        lock (_syncRoot)
        {
            // lock/Monitor 支持同线程重入，ReadProfiles 内部再次获取锁是安全的。
            var profiles = ReadProfiles().ToList();

            // 默认名称按当前索引数量生成，用户可以在实例卡片中修改。
            var profile = new BetterGiProfile
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"云原神 {profiles.Count + 1}" : name.Trim()
            };
            profiles.Add(profile);

            // 先登记索引，再创建目录和模块默认配置。
            SaveProfiles(profiles);
            EnsureProfileLayout(profile);
            WriteModuleConfig(profile.Id, new CloudGameModuleConfig());
            return profile;
        }
    }

    /// <summary>
    /// 新增或更新 Profile 基础信息，并同步写入实例目录中的 profile.json。
    /// </summary>
    public void SaveProfile(BetterGiProfile profile)
    {
        lock (_syncRoot)
        {
            // 使用可变列表合并当前 Profile 后再整体写回索引。
            var profiles = ReadProfiles().ToList();
            // Id 是更新已有项和追加新项的唯一判断依据。
            var index = profiles.FindIndex(x => x.Id == profile.Id);
            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }
            SaveProfiles(profiles);
            EnsureProfileLayout(profile);
        }
    }

    /// <summary>
    /// 读取指定 Profile 的云原神模块配置。
    /// </summary>
    public CloudGameModuleConfig ReadModuleConfig(Guid profileId)
    {
        lock (_syncRoot)
        {
            // 模块文件固定存放在实例的 Modules 目录。
            var path = GetCloudGameModulePath(profileId);
            if (!File.Exists(path))
            {
                // 老 Profile 或首次创建时使用模块默认值。
                return new CloudGameModuleConfig();
            }

            try
            {
                return JsonConvert.DeserializeObject<CloudGameModuleConfig>(File.ReadAllText(path))
                       ?? new CloudGameModuleConfig();
            }
            catch
            {
                // 模块配置异常时回退默认值，不影响其他 Profile。
                return new CloudGameModuleConfig();
            }
        }
    }

    /// <summary>
    /// 写入指定 Profile 的云原神模块配置。
    /// </summary>
    public void WriteModuleConfig(Guid profileId, CloudGameModuleConfig config)
    {
        lock (_syncRoot)
        {
            Directory.CreateDirectory(GetModulesPath(profileId));
            File.WriteAllText(GetCloudGameModulePath(profileId),
                JsonConvert.SerializeObject(config, Formatting.Indented));
        }
    }

    /// <summary>
    /// 获取并确保当前 Profile 独占的 WebView2 用户数据目录存在。
    /// </summary>
    public string GetWebView2DataPath(Guid profileId)
    {
        // UDF 位于实例目录内，不与其他 Profile 共享登录态。
        var path = Path.Combine(GetProfilePath(profileId), "WebView2Data");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 获取 Profile 根目录，目录名使用无分隔符 Guid。
    /// </summary>
    public string GetProfilePath(Guid profileId)
    {
        return Path.Combine(_profilesRoot, profileId.ToString("N"));
    }

    /// <summary>
    /// 获取 Profile 的模块配置目录。
    /// </summary>
    private string GetModulesPath(Guid profileId)
    {
        return Path.Combine(GetProfilePath(profileId), "Modules");
    }

    /// <summary>
    /// 获取云原神模块配置文件路径。
    /// </summary>
    private string GetCloudGameModulePath(Guid profileId)
    {
        return Path.Combine(GetModulesPath(profileId), "CloudGame.json");
    }

    /// <summary>
    /// 确保通用 Profile 根目录存在。
    /// </summary>
    private void EnsureRoot()
    {
        Directory.CreateDirectory(_profilesRoot);
    }

    /// <summary>
    /// 确保通用 Profile 目录存在。
    /// config.json 仅预留文件位置，本期不会创建或读取它。
    /// </summary>
    private void EnsureProfileLayout(BetterGiProfile profile)
    {
        // 基础目录名与 ProfileId 一一对应。
        var path = GetProfilePath(profile.Id);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "WebView2Data"));
        Directory.CreateDirectory(Path.Combine(path, "Modules"));
        // 实例目录内保留一份基础信息，便于未来脱离全局索引迁移。
        File.WriteAllText(Path.Combine(path, "profile.json"),
            JsonConvert.SerializeObject(profile, Formatting.Indented));

        // config.json 仅为未来 BetterGI 全局多配置预留，本期仍使用 User/config.json。
    }

    /// <summary>
    /// 将完整 Profile 集合写回全局索引。
    /// </summary>
    private void SaveProfiles(IReadOnlyCollection<BetterGiProfile> profiles)
    {
        EnsureRoot();
        File.WriteAllText(_indexPath, JsonConvert.SerializeObject(profiles, Formatting.Indented));
    }
}
