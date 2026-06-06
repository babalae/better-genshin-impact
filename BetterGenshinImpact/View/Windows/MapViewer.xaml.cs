using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    private const double DefaultVisibleSideColumnWidth = 580;

    public MapViewerViewModel ViewModel { get; }
    private bool _isRecorderConnectionRegistered;
    private double _lastVisibleSideColumnWidth = DefaultVisibleSideColumnWidth;
    private bool _isApplyingSidePanelLayout;

    public MapViewer(string mapName)
    {
        DataContext = ViewModel = new MapViewerViewModel(mapName);
        ViewModel.DialogOwner = this;
        InitializeComponent();
        RegisterRecorderConnection();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += MapViewer_Loaded;
        SizeChanged += MapViewer_SizeChanged;
        Closing += MapViewer_Closing;
        Closed += MapViewer_Closed;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
            else if (msg.PropertyName == "SelectAllRecordedWaypointRows")
            {
                RecordedWaypointGrid.SelectAll();
                ViewModel.SyncRecordedWaypointSelection(RecordedWaypointGrid.SelectedItems);
            }
            else if (msg.PropertyName == "ClearRecordedWaypointRowsSelection")
            {
                RecordedWaypointGrid.SelectedItems.Clear();
                ViewModel.SyncRecordedWaypointSelection(RecordedWaypointGrid.SelectedItems);
            }
            else if (msg.PropertyName == "SelectRecordedWaypointRows" && msg.NewValue is IEnumerable<RecordedWaypointViewModel> selectedWaypoints)
            {
                RecordedWaypointGrid.SelectedItems.Clear();
                foreach (var selectedWaypoint in selectedWaypoints)
                {
                    RecordedWaypointGrid.SelectedItems.Add(selectedWaypoint);
                }

                ViewModel.SyncRecordedWaypointSelection(RecordedWaypointGrid.SelectedItems);
            }
        });
    }

    private void MapViewer_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySidePanelLayout(captureVisibleWidth: false);
    }

    private void MapViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!ViewModel.IsSidePanelVisible || MapColumn.Width.GridUnitType != GridUnitType.Star)
        {
            ApplySidePanelLayout(captureVisibleWidth: ViewModel.IsSidePanelVisible);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewerViewModel.IsSidePanelVisible))
        {
            ApplySidePanelLayout(captureVisibleWidth: !ViewModel.IsSidePanelVisible);
        }
    }

    private void SidePanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!ViewModel.IsSidePanelVisible)
        {
            return;
        }

        CaptureVisibleSideColumnWidth();
        ApplySidePanelLayout(captureVisibleWidth: false);
    }

    private void ApplySidePanelLayout(bool captureVisibleWidth)
    {
        if (_isApplyingSidePanelLayout)
        {
            return;
        }

        _isApplyingSidePanelLayout = true;
        try
        {
            if (ViewModel.IsSidePanelVisible)
            {
                if (captureVisibleWidth)
                {
                    CaptureVisibleSideColumnWidth();
                }

                var splitterWidth = ViewModel.SplitterColumnWidth.Value;
                var minSideWidth = GetEffectiveSideColumnMinWidth(splitterWidth);
                SideColumn.MinWidth = minSideWidth;
                SideColumn.Width = new GridLength(GetVisibleSideColumnWidth(splitterWidth, minSideWidth));
                SplitterColumn.Width = ViewModel.SplitterColumnWidth;
                MapColumn.Width = ViewModel.MapColumnWidth;
                return;
            }

            if (captureVisibleWidth)
            {
                CaptureVisibleSideColumnWidth();
            }

            SideColumn.MinWidth = 0;
            SideColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            MapColumn.Width = ViewModel.MapColumnWidth;
        }
        finally
        {
            _isApplyingSidePanelLayout = false;
        }
    }

    private void CaptureVisibleSideColumnWidth()
    {
        var width = SideColumn.ActualWidth;
        if (width <= 0 && SideColumn.Width.IsAbsolute)
        {
            width = SideColumn.Width.Value;
        }

        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return;
        }

        _lastVisibleSideColumnWidth = Math.Max(width, ViewModel.SideColumnMinWidth);
    }

    private double GetEffectiveSideColumnMinWidth(double splitterWidth)
    {
        if (MainLayoutGrid.ActualWidth <= 0)
        {
            return ViewModel.SideColumnMinWidth;
        }

        var maxSideWidth = Math.Max(0, MainLayoutGrid.ActualWidth - MapColumn.MinWidth - splitterWidth);
        return Math.Min(ViewModel.SideColumnMinWidth, maxSideWidth);
    }

    private double GetVisibleSideColumnWidth(double splitterWidth, double minSideWidth)
    {
        var width = Math.Max(_lastVisibleSideColumnWidth, minSideWidth);
        if (MainLayoutGrid.ActualWidth <= 0)
        {
            return width;
        }

        var maxSideWidth = Math.Max(0, MainLayoutGrid.ActualWidth - MapColumn.MinWidth - splitterWidth);
        return Math.Max(0, Math.Min(width, maxSideWidth));
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
        var modifiers = Keyboard.Modifiers;
        if (ShouldCommitFocusedTextInput(key, modifiers))
        {
            CommitFocusedTextInput();
        }

        if (ViewModel.HandleRecorderShortcut(
                key,
                modifiers,
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

    private void RecordedWaypointGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        if (!row.IsSelected)
        {
            RecordedWaypointGrid.SelectedItems.Clear();
            row.IsSelected = true;
            RecordedWaypointGrid.SelectedItem = row.Item;
        }

        row.Focus();
        ViewModel.SyncRecordedWaypointSelection(RecordedWaypointGrid.SelectedItems);
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static bool ShouldCommitFocusedTextInput(Key key, ModifierKeys modifiers)
    {
        return modifiers.HasFlag(ModifierKeys.Control) && key == Key.S;
    }

    private static void CommitFocusedTextInput()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return;
        }

        for (var current = focused; current != null; current = GetParent(current))
        {
            var binding = GetTextBindingExpression(current);
            if (binding == null)
            {
                continue;
            }

            binding.UpdateSource();
            return;
        }
    }

    private static BindingExpressionBase? GetTextBindingExpression(DependencyObject current)
    {
        if (current is TextBox textBox)
        {
            return textBox.GetBindingExpression(TextBox.TextProperty);
        }

        var textProperty = current.GetType()
            .GetField("TextProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as DependencyProperty;
        return textProperty == null
            ? null
            : BindingOperations.GetBindingExpression(current, textProperty);
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
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

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
