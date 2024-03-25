using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

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
            return (bool)_sourceObject.GetType().GetProperty(_propertyName).GetValue(_sourceObject);
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
