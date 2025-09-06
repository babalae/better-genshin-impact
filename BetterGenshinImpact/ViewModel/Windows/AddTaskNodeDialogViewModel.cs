using System;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AddTaskNodeDialogViewModel : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "任务名称不能为空")]
    private string _taskName = "";

    [ObservableProperty]
    private string _taskDescription = "";

    [ObservableProperty]
    private string _taskType = "";

    public event EventHandler<bool>? RequestClose;

    public AddTaskNodeDialogViewModel()
    {
    }

    public AddTaskNodeDialogViewModel(string taskType)
    {
        TaskType = taskType;
        TaskName = GetDefaultTaskName(taskType);
    }

    private string GetDefaultTaskName(string taskType)
    {
        return taskType switch
        {
            "Javascript" => "新建JS脚本",
            "Pathing" => "新建地图追踪任务",
            "KeyMouse" => "新建键鼠脚本",
            "Shell" => "新建Shell脚本",
            "CSharp" => "新建C#方法",
            _ => "新建任务"
        };
    }

    [RelayCommand]
    private void Confirm()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TaskName))
        {
            return;
        }

        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }
}