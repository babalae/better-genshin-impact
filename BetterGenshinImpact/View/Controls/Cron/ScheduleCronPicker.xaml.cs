using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls.Cron;

public partial class ScheduleCronPicker : UserControl
{
    public static readonly DependencyProperty CronExpressionProperty = DependencyProperty.Register(
        nameof(CronExpression),
        typeof(string),
        typeof(ScheduleCronPicker),
        new FrameworkPropertyMetadata(
            "0 0 8 * * ?",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCronExpressionChanged
        )
    );

    public string CronExpression
    {
        get => (string)GetValue(CronExpressionProperty);
        set => SetValue(CronExpressionProperty, value);
    }

    public ScheduleCronPickerViewModel ViewModel { get; } = new();

    public ScheduleCronPicker()
    {
        InitializeComponent();
        DataContext = this;

        ViewModel.ApplyRequested += (_, cron) => CronExpression = cron;
        ViewModel.LoadFromCron(CronExpression);
    }

    private static void OnCronExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScheduleCronPicker control)
        {
            return;
        }

        control.ViewModel.LoadFromCron(e.NewValue as string);
    }
}

