using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Script.WebView;

/// <summary>
/// 给 WebView 提供的桥接类
/// 用于调用 C# 方法
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class RepoWebBridge
{
    public async Task<string> GetRepoJson()
    {
        try
        {
            if (!Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                throw new Exception("仓库文件夹不存在，请至少成功更新一次仓库！");
            }

            var localRepoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (localRepoJsonPath is null)
            {
                throw new Exception("repo.json 仓库索引文件不存在，请至少成功更新一次仓库！");
            }

            var json = await File.ReadAllTextAsync(localRepoJsonPath);
            return json;
        }
        catch (Exception e)
        {
            await MessageBox.ShowAsync(e.Message, "获取仓库信息失败");
            return "";
        }
    }

    public async void ImportUri(string url)
    {
        try
        {
            await ScriptRepoUpdater.Instance.ImportScriptFromUri(url, false);
            WeakReferenceMessenger.Default.Send(new RefreshDataMessage("Refresh"));
        }
        catch (Exception e)
        {
            await MessageBox.ShowAsync(e.Message, "订阅脚本链接失败！");
        }
    }

    public async Task<string> GetUserConfigJson()
    {
        try
        {
            string userConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "User", "config.json");
            if (!File.Exists(userConfigPath))
            {
                throw new Exception("用户配置文件不存在: " + userConfigPath);
            }
            return await File.ReadAllTextAsync(userConfigPath);
        }
        catch (Exception e)
        {
            await MessageBox.ShowAsync(e.Message, "获取用户配置失败");
            return "";
        }
    }
}
