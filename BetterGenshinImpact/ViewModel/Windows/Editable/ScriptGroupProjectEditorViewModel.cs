using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Editable;

public class ScriptGroupProjectEditorViewModel : ObservableObject
{
    private readonly ScriptGroupProject _project;
    private readonly NotificationConfig _globalNotificationConfig;

    public bool GlobalJsNotificationEnabled 
        => _globalNotificationConfig.JsNotificationEnabled;

    public List<KeyValuePair<bool, string>> JsNotificationOptions { get; } = new()
    {
        new KeyValuePair<bool, string>(true, App.GetService<ILocalizationService>().GetString("common.enable")),
        new KeyValuePair<bool, string>(false, App.GetService<ILocalizationService>().GetString("common.disable"))
    };
    
    public bool IsJsScript => _project.Type == "Javascript";
    public bool? AllowJsNotification
    {
        get => _project.AllowJsNotification;
        set
        {
            if (!GlobalJsNotificationEnabled) return;
            _project.AllowJsNotification = value;
            OnPropertyChanged();
        }
    }
    
    public string Status
    {
        get => _project.Status;
        set
        {
            if (_project.Status != value)
            {
                _project.Status = value;
                OnPropertyChanged();
            }
        }
    }
    public ScriptGroupProjectEditorViewModel(ScriptGroupProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _globalNotificationConfig = TaskContext.Instance().Config.NotificationConfig;
        // 监听全局配置变更
        _project.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ScriptGroupProject.AllowJsNotification))
            {
                OnPropertyChanged(nameof(AllowJsNotification));
            }
        };
    }
}
