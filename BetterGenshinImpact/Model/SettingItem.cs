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

        var label = new TextBlock
        {
            Text = Label,
            Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap
        };
        list.Add(label);

        var binding = new Binding
        {
            Source = context,
            Path = new PropertyPath(Name)
        };
        switch (Type)
        {
            case "input-text":
                var textBox = new TextBox
                {
                    Name = Name,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };
                if (Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        ctx.TryAdd(Name, Default.ToString());
                    }
                }

                BindingOperations.SetBinding(textBox, TextBox.TextProperty, binding);
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

                if (Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        ctx.TryAdd(Name, Default.ToString());
                    }
                }

                BindingOperations.SetBinding(comboBox, Selector.SelectedItemProperty, binding);
                list.Add(comboBox);
                break;

            case "checkbox":
                var checkBox = new CheckBox
                {
                    Name = Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                if (Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        if (bool.TryParse(Default.ToString(), out var value))
                        {
                            ctx.TryAdd(Name, value);
                        }
                    }
                }

                BindingOperations.SetBinding(checkBox, ToggleButton.IsCheckedProperty, binding);
                list.Add(checkBox);
                break;

            default:
                throw new Exception($"Unknown setting type: {Type}");
        }

        return list;
    }
}