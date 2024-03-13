using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.Forms.MessageBox;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickMonoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _jsonText = string.Empty;

    [ObservableProperty]
    private string _jsonPath = string.Empty;

    public AutoPickMonoViewModel(string path)
    {
        try
        {
            JsonPath = path;
            JsonText = Global.ReadAllTextIfExist(JsonPath)!;
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
            _ = JsonSerializer.Deserialize<IEnumerable<string>>(JsonText, ConfigService.JsonOptions) ?? [];
        }
        catch (Exception e)
        {
            MessageBox.Show("保存失败：" + e.ToString());
            return;
        }

        try
        {
            Global.WriteAllText(JsonPath, JsonText);
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
        Application.Current.Windows
            .Cast<Window>()
            .FirstOrDefault(w => w.Tag?.Equals(nameof(AutoPickMonoDialog)) ?? false)
            ?.Close();
    }
}
