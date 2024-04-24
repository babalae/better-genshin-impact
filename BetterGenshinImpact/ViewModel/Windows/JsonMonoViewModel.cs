using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class JsonMonoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _jsonText = string.Empty;

    [ObservableProperty]
    private string _jsonPath = string.Empty;

    public JsonMonoViewModel(string path)
    {
        try
        {
            JsonPath = path;
            JsonText = Global.ReadAllTextIfExist(JsonPath)!;
        }
        catch (Exception e)
        {
            WpfUiMessageBoxHelper.Show("读取黑白名单出错：" + e.ToString());
        }
    }

    [RelayCommand]
    public void Save()
    {
        try
        {
            JsonSerializer.Deserialize<object>(JsonText, ConfigService.JsonOptions);
        }
        catch (Exception e)
        {
            WpfUiMessageBoxHelper.Show("保存失败：" + e.ToString());
            return;
        }

        try
        {
            Global.WriteAllText(JsonPath, JsonText);
            // WpfUiMessageBoxHelper.Show("保存成功");
        }
        catch (Exception e)
        {
            WpfUiMessageBoxHelper.Show("保存失败：" + e.ToString());
        }
    }

    [RelayCommand]
    public void Close()
    {
        Application.Current.Windows
            .Cast<Window>()
        .FirstOrDefault(w => w.Tag?.Equals(nameof(JsonMonoDialog)) ?? false)
        ?.Close();
    }
}
