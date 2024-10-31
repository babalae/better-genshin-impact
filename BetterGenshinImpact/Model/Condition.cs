using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model;

public partial class Condition : ObservableObject
{
    [ObservableProperty]
    private string? _subject;// 主体

    [ObservableProperty]
    private string _predicate = "包含"; // 谓语

    [ObservableProperty]
    private ObservableCollection<string>? _object; // 宾语

    [ObservableProperty]
    private string? _result; // 条件结果

    public ConditionDefinition Definition => string.IsNullOrEmpty(Subject) ? new ConditionDefinition() : ConditionDefinitions.Definitions[Subject];

    partial void OnSubjectChanged(string? value)
    {
        Object?.Clear();
        // 通知 Definition 属性的变化
        OnPropertyChanged(nameof(Definition));
    }
}
