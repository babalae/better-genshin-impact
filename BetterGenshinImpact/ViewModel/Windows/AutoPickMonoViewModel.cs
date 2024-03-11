using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickMonoViewModel : ObservableObject
{
    [ObservableProperty]
    private string blackJson = string.Empty;

    [ObservableProperty]
    private string whiteJson = string.Empty;

    public AutoPickMonoViewModel()
    {
        try
        {
            BlackJson = Global.ReadAllTextIfExist(@"User\pick_black_lists.json")!;
            WhiteJson = Global.ReadAllTextIfExist(@"User\pick_white_lists.json")!;
        }
        catch (Exception e)
        {
            MessageBox.Show("读取黑白名单出错：" + e.ToString());
        }
    }

    [RelayCommand]
    public void Save()
    {
        try
        {
            _ = JsonSerializer.Deserialize<IEnumerable<string>>(BlackJson, ConfigService.JsonOptions) ?? [];
            _ = JsonSerializer.Deserialize<IEnumerable<string>>(WhiteJson, ConfigService.JsonOptions) ?? [];
        }
        catch (Exception e)
        {
            MessageBox.Show("保存失败：" + e.ToString());
            return;
        }

        try
        {
            Global.WriteAllText(@"User\pick_black_lists.json", BlackJson);
            Global.WriteAllText(@"User\pick_white_lists.json", WhiteJson);
            MessageBox.Show("保存成功", "自动拾取");
        }
        catch (Exception e)
        {
            MessageBox.Show("保存失败：" + e.ToString(), "自动拾取");
        }
    }

    [RelayCommand]
    public void Close()
    {
        System.Windows.Application.Current.Windows
            .Cast<System.Windows.Window>()
            .Where(w => w.Tag?.Equals(nameof(AutoPickMonoDialog)) ?? false)
            .FirstOrDefault()
            ?.Close();
    }
}
