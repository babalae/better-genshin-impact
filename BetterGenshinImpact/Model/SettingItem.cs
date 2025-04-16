using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace BetterGenshinImpact.Model;

[Serializable]
public class SettingItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public List<string>? Options { get; set; }

    public object? Default { get; set; }

    public List<UIElement> ToControl(dynamic context)
    {
        var list = new List<UIElement>();

        var label = new Label
        {
            Content = Label,
            Margin = new Thickness(0, 0, 0, 5)
        };
        list.Add(label);

        var isEmpty = context is IDictionary<string, object?> { Count: 0 };
        var binding = new Binding
        {
            Source = context,
            Path = new PropertyPath(Name),
            Mode = isEmpty ? BindingMode.OneWayToSource : BindingMode.TwoWay
        };
        switch (Type)
        {
            case "input-text":
                var textBox = new TextBox
                {
                    Name = Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                BindingOperations.SetBinding(textBox, TextBox.TextProperty, binding);
                if (isEmpty && Default != null)
                {
                    textBox.Text = Default.ToString()!;
                }

                list.Add(textBox);
                break;

            case "select":
                var comboBox = new ComboBox
                {
                    Name = Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                if (Options != null)
                {
                    foreach (var option in Options)
                    {
                        comboBox.Items.Add(option);
                    }
                }

                BindingOperations.SetBinding(comboBox, Selector.SelectedItemProperty, binding);
                if (isEmpty && Default != null)
                {
                    comboBox.SelectedItem = Default.ToString()!;
                }

                list.Add(comboBox);
                break;

            case "checkbox":
                var checkBox = new CheckBox
                {
                    Name = Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                BindingOperations.SetBinding(checkBox, ToggleButton.IsCheckedProperty, binding);
                if (isEmpty && Default != null)
                {
                    checkBox.IsChecked = bool.TryParse(Default.ToString()!, out var value) && value;
                }

                list.Add(checkBox);
                break;

            default:
                throw new Exception($"Unknown setting type: {Type}");
        }

        return list;
    }
}