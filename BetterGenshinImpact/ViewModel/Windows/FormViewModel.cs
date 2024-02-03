using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.ViewModel.Windows;

public abstract partial class FormViewModel<T> : ObservableObject
{
    [ObservableProperty] private ObservableCollection<T> _list;

    protected FormViewModel()
    {
        _list = new();
    }

    public void AddRange(List<T> itemList)
    {
        foreach (var item in itemList)
        {
            List.Add(item);
        }
    }

    /// <summary>
    /// 默认添加至头部
    /// </summary>
    /// <param name="item"></param>
    [RelayCommand]
    public void OnAdd(T item)
    {
        List.Insert(0, item);
    }

    [RelayCommand]
    public void OnRemoveAt(int index)
    {
        List.RemoveAt(index);
    }

    [RelayCommand]
    public void OnEditAt(int index)
    {
        // 修改某行数据
        // 弹出编辑窗口
        // ...

        // 保存结果
        // List[index] = ?;
    }

    [RelayCommand]
    public void OnSave()
    {
        // 保存整个改动后的结果
    }
}