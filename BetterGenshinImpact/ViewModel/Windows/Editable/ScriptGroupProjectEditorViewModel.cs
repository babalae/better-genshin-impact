using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Documents;
using System.Windows.Media;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
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
        new KeyValuePair<bool, string>(true, "启用"),
        new KeyValuePair<bool, string>(false, "禁用")
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
    
    public bool? AllowJsHTTP
    {
        get
        {
            return _project.AllowJsHTTP;
        }
        set
        {
            // 为了避免误用，AllowJsHTTP禁止set，通过更新Hash来控制
            // 脚本作者更新时，如果Hash变更会自动禁用http权限，避免安全风险
            if (value == null || value == false)
            {
                _project.AllowJsHTTPHash = null;
            }
            else
            {
                _project.AllowJsHTTPHash = _project.GetHttpAllowedUrlsHash();
            }
            OnPropertyChanged();
        }
    }

    public record JsText(string Text, Brush Color);
    public List<JsText> JsHTTPInfoText
    {
        get
        {
            if (_project.Project == null)
            {
                _project.BuildScriptProjectRelation();
            }
            if (_project.Project == null)
            {
                return new List<JsText>
                {
                    new JsText("当前脚本项目未加载", Brushes.Red)
                };
            }
            var urls = _project.Project.Manifest?.HttpAllowedUrls ?? [];
            if (urls.Length == 0)
            {
                return new List<JsText>
                {
                    new JsText("当前脚本无需使用HTTP资源", Brushes.Green)
                };
            }
            return new List<JsText>
            {
                new JsText($"当前脚本使用 {urls.Length} 个HTTP资源", Brushes.OrangeRed)
            };
        }
    }

    public record JsLine(string Text, Brush Color);

    public List<JsLine> JsHTTPInfo
    {
        get
        {
            if (_project.Project == null)
            {
                _project.BuildScriptProjectRelation();
            }
            var urls = _project.Project?.Manifest?.HttpAllowedUrls ?? [];
            var blocks = new List<JsLine>();
            foreach (var url in urls)
            {
                blocks.Add(new JsLine(url, Brushes.OrangeRed));
            }
            return blocks;
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
            if (e.PropertyName == nameof(ScriptGroupProject.AllowJsHTTPHash))
            {
                OnPropertyChanged(nameof(AllowJsHTTP));
            }
        };
    }
}
