using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BetterGenshinImpact.View.Controls;

namespace BetterGenshinImpact.Model;

[Serializable]
public class SettingItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public List<string>? Options { get; set; }

    public Dictionary<string, List<string>>? CascadeOptions { get; set; }

    public object? Default { get; set; }

    public List<UIElement> ToControl(dynamic context)
    {
        var list = new List<UIElement>();

        if (!String.IsNullOrEmpty(Label))
        {
            var label = new TextBlock
            {
                Text = Label,
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            list.Add(label);
        }

        var binding = new Binding
        {
            Source = context,
            Path = new PropertyPath(Name)
        };
        switch (Type)
        {
            case "separator":
                list.Add(new Separator
                {
                    Margin = new Thickness(0, 0, 0, 2)
                });
                break;

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

            case "multi-checkbox":
                {
                    var checkedValues = new List<string>();
                    if (context is IDictionary<string, object?> ctx)
                    {
                        if (!ctx.ContainsKey(Name))
                        {
                            if (Default is JsonElement j)
                            {
                                ctx[Name] = j.Deserialize<List<string>>();
                            }
                            else
                            {
                                ctx[Name] = new List<string>();
                            }
                        }
                        else if (ctx[Name] is List<object> listOfObjects)
                        {
                            ctx[Name] = listOfObjects.Select(i => (string)i).ToList();
                        }
                        checkedValues = (List<string>)ctx[Name]!;
                    }
                    var wrapPanel = new WrapPanel
                    {
                        Orientation = Orientation.Horizontal
                    };
                    if (Options != null)
                    {
                        foreach (var option in Options)
                        {
                            var box = new CheckBox
                            {
                                Content = option,
                                IsChecked = checkedValues.Contains(option),
                            };
                            RoutedEventHandler callback = (sender, e) =>
                            {
                                bool isChecked = ((CheckBox)sender).IsChecked ?? false;
                                if (isChecked && !checkedValues.Contains(option))
                                {
                                    checkedValues.Add(option);
                                }
                                else if (!isChecked)
                                {
                                    checkedValues.Remove(option);
                                }
                            };
                            box.Checked += callback;
                            box.Unchecked += callback;
                            wrapPanel.Children.Add(box);
                        }
                    }
                    list.Add(wrapPanel);
                    break;
                }

            case "cascade-select":
                {
                    if (CascadeOptions == null || CascadeOptions.Count == 0)
                    {
                        break;
                    }

                    var cascadeSelector = new CascadeSelector
                    {
                        CascadeOptions = CascadeOptions,
                        DefaultValue = Default?.ToString(),
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    if (Default != null)
                    {
                        if (context is IDictionary<string, object?> ctx)
                        {
                            ctx.TryAdd(Name, Default.ToString());
                        }
                    }

                    BindingOperations.SetBinding(cascadeSelector, CascadeSelector.SelectedValueProperty, 
                        new Binding(Name) { Source = context, Mode = BindingMode.TwoWay });

                    list.Add(cascadeSelector);
                    break;
                }

            default:
                throw new Exception($"Unknown setting type: {Type}");
        }

        return list;
    }
}