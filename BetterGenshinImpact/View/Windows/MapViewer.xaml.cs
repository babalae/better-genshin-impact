using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    public MapViewerViewModel ViewModel { get; }
    private bool _isRecorderConnectionRegistered;

    public MapViewer(string mapName)
    {
        DataContext = ViewModel = new MapViewerViewModel(mapName);
        ViewModel.DialogOwner = this;
        InitializeComponent();
        RegisterRecorderConnection();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        Closing += MapViewer_Closing;
        Closed += MapViewer_Closed;
        PreviewKeyDown += MapViewer_PreviewKeyDown;
        RecordedWaypointGrid.SelectionChanged += RecordedWaypointGrid_SelectionChanged;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "OpenWaypointAdvancedEditor" && msg.NewValue is RecordedWaypointViewModel waypoint)
            {
                var window = new WaypointAdvancedEditorWindow(waypoint)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
        });
    }

    private void RegisterRecorderConnection()
    {
        if (_isRecorderConnectionRegistered)
        {
            return;
        }

        PathRecorder.Instance.RegisterWpfEditor();
        _isRecorderConnectionRegistered = true;
    }

    private void MapViewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ViewModel.HandleRecorderShortcut(
                key,
                Keyboard.Modifiers,
                RecordedWaypointGrid.SelectedItems,
                IsTextInputFocused()))
        {
            e.Handled = true;
        }
    }

    private void MapEditorComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        comboBox.Focus();
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void RecordedWaypointGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SyncRecordedWaypointSelection(RecordedWaypointGrid.SelectedItems);
    }

    private void TitleSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsViewSettingsOpen = !ViewModel.IsViewSettingsOpen;
    }

    private void TitleTopmostButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsTopmost = !ViewModel.IsTopmost;
    }

    private void ViewSettingsPopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel.IsViewSettingsOpen = false;
    }

    private void MapViewer_Closing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.ConfirmJsonEditsBeforeLeavingRecorder(this))
        {
            e.Cancel = true;
            return;
        }

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(ViewModel);
    }

    private void MapViewer_Closed(object? sender, EventArgs e)
    {
        if (!_isRecorderConnectionRegistered)
        {
            return;
        }

        PathRecorder.Instance.UnregisterWpfEditor();
        _isRecorderConnectionRegistered = false;
    }

    private static bool IsTextInputFocused()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return false;
        }

        for (var current = focused; current != null; current = GetParent(current))
        {
            if (current is TextBoxBase or PasswordBox or ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent != null)
            {
                return parent;
            }
        }

        return current is FrameworkElement frameworkElement
            ? frameworkElement.Parent
            : null;
    }
}
