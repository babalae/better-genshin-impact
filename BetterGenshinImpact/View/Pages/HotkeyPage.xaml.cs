using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// TaskSettingsPage.xaml 的交互逻辑
/// </summary>
public partial class HotkeyPage : Page
{
    public List<Setting> Settings { get; private set; }
    public HotkeyPage()
    {
        InitializeComponent();

        Settings = new List<Setting>();
        Settings.Add(new Setting("On/Off", true));
        Settings.Add(new Setting("Elevation", "100"));

        DataContext = this;
    }


}

public class Setting
{
    public Setting(string name, string value)
    {
        Name = name;
        Value = value;
        IsCheckBox = false;
    }

    public Setting(string name, bool value)
    {
        Name = name;
        Value = value;
        IsCheckBox = true;
    }

    public string Name { get; private set; }
    public object Value { get; set; }
    public bool IsCheckBox { get; private set; }
}

public class SettingsTemplateSelector : DataTemplateSelector
{
    public DataTemplate CheckBoxTemplate { get; set; }
    public DataTemplate TextBoxTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        Setting setting = item as Setting;
        if (setting.IsCheckBox)
        {
            return CheckBoxTemplate;
        }
        return TextBoxTemplate;
    }
}