using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class GuardianAvatarListViewModel : ObservableObject
{
    [ObservableProperty]
    private string _listText = string.Empty;

    public GuardianAvatarListViewModel()
    {
        // 文件不存在时先写入默认名单，方便首次打开即可编辑；
        // 写失败（如被其它进程占用）不阻断打开，直接进入只读编辑流程。
        try
        {
            var path = Global.Absolute(GuardianAvatarListStore.FileRelativePath);
            if (!File.Exists(path))
            {
                GuardianAvatarListStore.Save(GuardianAvatarListStore.Load());
            }
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("写入默认名单失败：" + e.Message, "盾奶位名单");
        }

        ListText = Global.ReadAllTextIfExist(GuardianAvatarListStore.FileRelativePath) ?? string.Empty;
    }

    [RelayCommand]
    public void Save()
    {
        // 逐行校验角色名：非注释、非空行的角色必须存在于 combat_avatar.json
        var invalidLines = ListText
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith('#'))
            .Where(line =>
            {
                var name = line;
                if (name.EndsWith("长按", StringComparison.Ordinal))
                {
                    name = name[..^"长按".Length].Trim();
                }

                return string.IsNullOrEmpty(name) || !DefaultAutoFightConfig.CombatAvatarNames.Contains(name);
            })
            .ToList();

        if (invalidLines.Count > 0)
        {
            ThemedMessageBox.Error(
                "名单中存在无法识别的角色行：\n" + string.Join("\n", invalidLines.Take(8)) +
                "\n请检查角色名是否为游戏内标准中文名（可先打开一次再关掉文件查看示例）。",
                "盾奶位名单格式错误");
            return;
        }

        try
        {
            GuardianAvatarListStore.SaveRawText(ListText);
            Toast.Success("盾奶位名单已保存，下次开战生效");
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("保存失败：" + e.Message, "盾奶位名单保存失败");
        }
    }

    [RelayCommand]
    public void RestoreDefault()
    {
        try
        {
            GuardianAvatarListStore.ResetToDefault();
            ListText = Global.ReadAllTextIfExist(GuardianAvatarListStore.FileRelativePath) ?? string.Empty;
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("还原默认名单失败：" + e.Message, "盾奶位名单");
        }
    }

    [RelayCommand]
    public void Close()
    {
        Application.Current.Windows
            .Cast<Window>()
            .FirstOrDefault(w => w.Tag?.Equals(nameof(GuardianAvatarListDialog)) ?? false)
            ?.Close();
    }
}
