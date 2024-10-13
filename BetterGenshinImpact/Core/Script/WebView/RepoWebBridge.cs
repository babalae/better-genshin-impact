using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Script.WebView;

/// <summary>
/// 给 WebView 提供的桥接类
/// 用于调用 C# 方法
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class RepoWebBridge
{
    public string GetRepoJson()
    {
        try
        {
            var localRepoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (localRepoJsonPath is null)
            {
                _ = ScriptRepoUpdater.Instance.UpdateCenterRepo().ConfigureAwait(false);
                localRepoJsonPath = Directory.GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                if (localRepoJsonPath is null)
                {
                    throw new Exception("本地仓库缺少 repo.json");
                }
            }

            var json = File.ReadAllText(localRepoJsonPath);
            return json;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "获取仓库信息失败！");
            return "";
        }
    }

    public void ImportUri(string url)
    {
        try
        {
            ScriptRepoUpdater.Instance.ImportScriptFromUri(url, false).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "订阅脚本链接失败！");
        }
    }
}
