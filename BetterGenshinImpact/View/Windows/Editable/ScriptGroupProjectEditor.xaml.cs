using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.ViewModel.Windows.Editable;

namespace BetterGenshinImpact.View.Windows.Editable;

public partial class ScriptGroupProjectEditor : UserControl
{
    public ScriptGroupProjectEditor(ScriptGroupProject project)
    {
        InitializeComponent();
        DataContext = new ScriptGroupProjectEditorViewModel(project);
    }
}
