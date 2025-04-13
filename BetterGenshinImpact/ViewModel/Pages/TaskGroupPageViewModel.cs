using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Database;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TaskGroupPageViewModel : ViewModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TaskGroupPageViewModel> _logger;

    [ObservableProperty] private ObservableCollection<TaskGroup> _taskGroups = new();

    [ObservableProperty] private TaskGroup? _selectedTaskGroup;

    [ObservableProperty] private bool _isEditing;

    public TaskGroupPageViewModel(ApplicationDbContext dbContext, ILogger<TaskGroupPageViewModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        LoadTaskGroups();
    }

    private void LoadTaskGroups()
    {
        var groups = _dbContext.TaskGroups
            .OrderBy(t => t.OrderIndex)
            .ToList();
        TaskGroups = new ObservableCollection<TaskGroup>(groups);
    }

    [RelayCommand]
    private void AddTaskGroup()
    {
        var newGroup = new TaskGroup
        {
            OrderIndex = TaskGroups.Count + 1,
            TaskName = "新任务组",
            Category = "默认"
        };
        TaskGroups.Add(newGroup);
        SelectedTaskGroup = newGroup;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditTaskGroup()
    {
        if (SelectedTaskGroup != null)
        {
            IsEditing = true;
        }
    }

    [RelayCommand]
    private async Task SaveTaskGroup()
    {
        if (SelectedTaskGroup == null) return;


        if (SelectedTaskGroup.Id == 0)
        {
            _dbContext.TaskGroups.Add(SelectedTaskGroup);
        }
        else
        {
            _dbContext.TaskGroups.Update(SelectedTaskGroup);
        }

        await _dbContext.SaveChangesAsync();
        IsEditing = false;
        LoadTaskGroups();
    }

    [RelayCommand]
    private async Task DeleteTaskGroup()
    {
        if (SelectedTaskGroup == null) return;


        _dbContext.TaskGroups.Remove(SelectedTaskGroup);
        await _dbContext.SaveChangesAsync();
        TaskGroups.Remove(SelectedTaskGroup);
        SelectedTaskGroup = null;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        LoadTaskGroups();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedTaskGroup == null) return;

        var currentIndex = TaskGroups.IndexOf(SelectedTaskGroup);
        if (currentIndex > 0)
        {
            var previousGroup = TaskGroups[currentIndex - 1];
            (SelectedTaskGroup.OrderIndex, previousGroup.OrderIndex) =
                (previousGroup.OrderIndex, SelectedTaskGroup.OrderIndex);
            TaskGroups.Move(currentIndex, currentIndex - 1);
            _dbContext.SaveChanges();
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedTaskGroup == null) return;

        var currentIndex = TaskGroups.IndexOf(SelectedTaskGroup);
        if (currentIndex < TaskGroups.Count - 1)
        {
            var nextGroup = TaskGroups[currentIndex + 1];
            (SelectedTaskGroup.OrderIndex, nextGroup.OrderIndex) = (nextGroup.OrderIndex, SelectedTaskGroup.OrderIndex);
            TaskGroups.Move(currentIndex, currentIndex + 1);
            _dbContext.SaveChanges();
        }
    }
}