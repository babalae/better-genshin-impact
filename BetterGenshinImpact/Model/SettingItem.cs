using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

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

                    var firstLevelOptions = CascadeOptions.Keys.ToList();
                    var stackPanel = new StackPanel
                    {
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    var grid = new Grid();
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition());

                    var toggleButton = new ToggleButton
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(12, 6, 12, 6),
                        Height = 34,
                        Background = SystemColors.ControlBrush,
                        BorderBrush = SystemColors.ControlDarkBrush,
                        BorderThickness = new Thickness(1)
                    };

                    var toggleContent = new Grid();
                    toggleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    toggleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var selectedTextBlock = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    BindingOperations.SetBinding(selectedTextBlock, TextBlock.TextProperty, new Binding(Name)
                    {
                        Source = context,
                        TargetNullValue = "请选择",
                        FallbackValue = "请选择"
                    });

                    var chevronIcon = new TextBlock
                    {
                        Text = "▼",
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    };

                    Grid.SetColumn(selectedTextBlock, 0);
                    Grid.SetColumn(chevronIcon, 1);
                    toggleContent.Children.Add(selectedTextBlock);
                    toggleContent.Children.Add(chevronIcon);
                    toggleButton.Content = toggleContent;

                    var popup = new Popup
                    {
                        StaysOpen = false,
                        AllowsTransparency = true,
                        PlacementTarget = toggleButton,
                        PopupAnimation = PopupAnimation.Slide
                    };

                    var popupBorder = new Border
                    {
                        Background = SystemColors.WindowBrush,
                        BorderBrush = SystemColors.ControlDarkBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(0, 4, 0, 0),
                        Padding = new Thickness(4),
                        MinHeight = 100,
                        MaxHeight = 300,
                        Width = 300
                    };

                    var dropShadow = new DropShadowEffect
                    {
                        BlurRadius = 10,
                        ShadowDepth = 2,
                        Direction = 270,
                        Color = Colors.Black,
                        Opacity = 0.2
                    };
                    popupBorder.Effect = dropShadow;

                    var innerGrid = new Grid();
                    innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                    innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var firstLevelListView = new ListView
                    {
                        BorderThickness = new Thickness(0)
                    };
                    ScrollViewer.SetVerticalScrollBarVisibility(firstLevelListView, ScrollBarVisibility.Auto);
                    firstLevelListView.ItemsSource = firstLevelOptions;

                    var firstLevelTemplate = new DataTemplate(typeof(TextBlock));
                    var firstLevelFactory = new FrameworkElementFactory(typeof(TextBlock));
                    firstLevelFactory.SetBinding(TextBlock.TextProperty, new Binding());
                    firstLevelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 4, 0, 4));
                    firstLevelTemplate.VisualTree = firstLevelFactory;
                    firstLevelListView.ItemTemplate = firstLevelTemplate;

                    var divider = new Rectangle
                    {
                        Width = 1,
                        Fill = SystemColors.ControlDarkBrush,
                        Margin = new Thickness(2, 0, 2, 0)
                    };

                    var secondLevelListView = new ListView
                    {
                        BorderThickness = new Thickness(0)
                    };
                    ScrollViewer.SetVerticalScrollBarVisibility(secondLevelListView, ScrollBarVisibility.Auto);

                    var secondLevelTemplate = new DataTemplate(typeof(TextBlock));
                    var secondLevelFactory = new FrameworkElementFactory(typeof(TextBlock));
                    secondLevelFactory.SetBinding(TextBlock.TextProperty, new Binding());
                    secondLevelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 4, 0, 4));
                    secondLevelTemplate.VisualTree = secondLevelFactory;
                    secondLevelListView.ItemTemplate = secondLevelTemplate;

                    Grid.SetColumn(firstLevelListView, 0);
                    Grid.SetColumn(divider, 1);
                    Grid.SetColumn(secondLevelListView, 2);
                    innerGrid.Children.Add(firstLevelListView);
                    innerGrid.Children.Add(divider);
                    innerGrid.Children.Add(secondLevelListView);
                    popupBorder.Child = innerGrid;
                    popup.Child = popupBorder;

                    Grid.SetRow(toggleButton, 0);
                    Grid.SetRow(popup, 1);
                    grid.Children.Add(toggleButton);
                    grid.Children.Add(popup);
                    stackPanel.Children.Add(grid);

                    BindingOperations.SetBinding(popup, Popup.IsOpenProperty, new Binding("IsChecked")
                    {
                        Source = toggleButton,
                        Mode = BindingMode.TwoWay
                    });

                    string? currentFirstLevel = null;
                    if (Default != null)
                    {
                        var defaultValue = Default.ToString();
                        if (!string.IsNullOrEmpty(defaultValue) && context is IDictionary<string, object?> ctx)
                        {
                            ctx.TryAdd(Name, defaultValue);
                            foreach (var kvp in CascadeOptions)
                            {
                                if (kvp.Value.Contains(defaultValue))
                                {
                                    currentFirstLevel = kvp.Key;
                                    firstLevelListView.SelectedItem = currentFirstLevel;
                                    secondLevelListView.ItemsSource = kvp.Value;
                                    secondLevelListView.SelectedItem = defaultValue;
                                    break;
                                }
                            }
                        }
                    }

                    firstLevelListView.SelectionChanged += (sender, e) =>
                    {
                        if (firstLevelListView.SelectedItem is string selectedFirstLevel)
                        {
                            currentFirstLevel = selectedFirstLevel;
                            if (CascadeOptions.TryGetValue(selectedFirstLevel, out var secondLevelOptions))
                            {
                                secondLevelListView.ItemsSource = secondLevelOptions;
                                secondLevelListView.SelectedItem = null;
                            }
                        }
                    };

                    secondLevelListView.SelectionChanged += (sender, e) =>
                    {
                        if (secondLevelListView.SelectedItem is string selectedSecondLevel)
                        {
                            if (context is IDictionary<string, object?> ctx)
                            {
                                ctx[Name] = selectedSecondLevel;
                            }
                            toggleButton.IsChecked = false;
                        }
                    };

                    list.Add(stackPanel);
                    break;
                }

            default:
                throw new Exception($"Unknown setting type: {Type}");
        }

        return list;
    }
}