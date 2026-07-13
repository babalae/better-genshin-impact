using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

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
            if (File.Exists(JsonPath))
            {
                JsonText = Global.ReadAllTextIfExist(JsonPath)!;
            }
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("读取黑白名单出错：" + e.ToString());
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
            ThemedMessageBox.Error("保存失败：" + e.ToString());
            return;
        }

        try
        {
            Global.WriteAllText(JsonPath, JsonText);
            Toast.Success("保存成功");
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("保存失败：" + e.ToString());
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
