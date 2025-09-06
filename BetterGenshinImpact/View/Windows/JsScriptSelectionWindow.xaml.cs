using System.Windows;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class JsScriptSelectionWindow : FluentWindow
{
    public JsScriptSelectionViewModel ViewModel { get; }
    
    public JsScriptInfo? SelectedScript => ViewModel.SelectedScript;
    
    public bool DialogResult { get; private set; }

    public JsScriptSelectionWindow()
    {
        ViewModel = new JsScriptSelectionViewModel();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedScript != null)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            Wpf.Ui.Violeta.Controls.Toast.Warning("请选择一个JS脚本");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}