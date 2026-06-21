using System;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class TaskDefinitionEditWindowViewModel : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "名称不能为空")]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    public event EventHandler<bool>? RequestClose;

    public TaskDefinitionEditWindowViewModel()
    {
    }

    public TaskDefinitionEditWindowViewModel(string name, string description = "")
    {
        Name = name;
        Description = description;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Name))
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