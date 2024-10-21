using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;

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
            var needUpdate = false;
            string? localRepoJsonPath = null;
            if (Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                localRepoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                if (localRepoJsonPath is null)
                {
                    needUpdate = true;
                }
            }
            else
            {
                needUpdate = true;
            }

            if (needUpdate)
            {
                await ScriptRepoUpdater.Instance.UpdateCenterRepo();
            }

            localRepoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (localRepoJsonPath is null)
            {
                throw new Exception("本地仓库缺少 repo.json");
            }
            var json = await File.ReadAllTextAsync(localRepoJsonPath);
            return json;
        }
        catch (Exception e)
        {
            await MessageBox.ShowAsync(e.Message, "获取仓库信息失败！");
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
}
