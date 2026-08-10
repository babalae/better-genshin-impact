using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Windows.Editable;

namespace BetterGenshinImpact.View.Windows.Editable;

public partial class ScriptGroupProjectEditor : UserControl
{
    public ScriptGroupProjectEditor(ScriptGroupProjectEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
