using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using static Vanara.PInvoke.User32;

namespace Model
{
    public partial class KeyBindingSettingModel:ObservableObject
    {

        /// <summary>
        /// 按键绑定值
        /// </summary>
        [ObservableProperty]
        private KeyId _keyValue;

        [ObservableProperty]
        private ObservableCollection<KeyBindingSettingModel> _children = [];

        public string ActionName { get; set; }

        public bool IsExpanded => true;

        /// <summary>
        /// 界面上显示是文件夹而不是按键绑定
        /// </summary>
        [ObservableProperty]
        private bool _isDirectory;

        public string ConfigPropertyName { get; set; }

        public KeyBindingSettingModel(string name)
        {
            IsDirectory = true;
            ActionName = name;
        }

        public KeyBindingSettingModel(string actionName,  string configPropertyName, KeyId keyValue)
        {
            ActionName = actionName;
            ConfigPropertyName = configPropertyName;
            KeyValue = keyValue;
            IsDirectory = false;
        }

    }
}
