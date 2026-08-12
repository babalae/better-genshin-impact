using BetterGenshinImpact.ViewModel.Pages;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Pages;

public partial class CommonSettingsPage : Page
{
    private CommonSettingsPageViewModel ViewModel { get; }
    private Window? _hostWindow;

    public CommonSettingsPage(CommonSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hostWindow = Window.GetWindow(this);
        if (_hostWindow == hostWindow)
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = hostWindow;
        if (_hostWindow != null)
        {
            _hostWindow.Deactivated += HostWindowOnDeactivated;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
    }

    private void HostWindowOnDeactivated(object? sender, EventArgs e)
    {
        if (CommitFocusedTextBox())
        {
            Keyboard.ClearFocus();
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.Deactivated -= HostWindowOnDeactivated;
        _hostWindow = null;
    }

    private bool CommitFocusedTextBox()
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement || !IsInsideThisPage(focusedElement))
        {
            return false;
        }

        var textBox = FindParentTextBox(focusedElement);
        if (textBox == null)
        {
            return false;
        }

        var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        binding?.UpdateSource();
        return binding != null;
    }

    private bool IsInsideThisPage(DependencyObject element)
    {
        for (DependencyObject? current = element; current != null; current = GetParent(current))
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }

        return false;
    }

    private static TextBox? FindParentTextBox(DependencyObject element)
    {
        for (DependencyObject? current = element; current != null; current = GetParent(current))
        {
            if (current is TextBox textBox)
            {
                return textBox;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        return element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
    }
}
