using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel;

namespace BetterGenshinImpact.Model
{
    public partial class StatusItem : ObservableObject
    {
        public string Name { get; set; }
        private INotifyPropertyChanged _sourceObject { get; set; }
        private string _propertyName { get; set; }

        [ObservableProperty] private bool _isEnabled;

        public StatusItem(string name, INotifyPropertyChanged sourceObject, string propertyName = "Enabled")
        {
            Name = name;
            _sourceObject = sourceObject;
            _propertyName = propertyName;

            _sourceObject.PropertyChanged += OnSourcePropertyChanged;
            IsEnabled = GetSourceValue();
        }

        private bool GetSourceValue()
        {
            var property = _sourceObject.GetType().GetProperty(_propertyName);
            ArgumentNullException.ThrowIfNull(property);
            var value = property.GetValue(_sourceObject);
            ArgumentNullException.ThrowIfNull(value);
            return (bool)value;
        }


        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _propertyName)
            {
                this.IsEnabled = GetSourceValue();
            }
        }
    }
}
