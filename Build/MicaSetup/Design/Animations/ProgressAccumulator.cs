using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MicaSetup.Controls.Animations;

public partial class ProgressAccumulator : ObservableObject, IDisposable
{
    public Action<double>? Handler;
    public DoubleEasingAnimation? Animation;

    [ObservableProperty]
    private double duration = 3000d;

    [ObservableProperty]
    private double current = 0d;

    [ObservableProperty]
    private double from = 0d;

    [ObservableProperty]
    private double to = 100d;

    private Task task = null!;
    private bool isRunning = false;
    private DateTime startTime = default;
    private double durationTime = default;

    public ProgressAccumulator(double from = 0d, double to = 100d, double duration = 3000d, Action<double> handler = null!, DoubleEasingAnimation anime = null!)
    {
        Reset(from, to, duration, handler);
        Animation = anime;
    }

    public void Dispose()
    {
        Reset();
    }

    public ProgressAccumulator Start()
    {
        isRunning = true;
        startTime = DateTime.Now;
        durationTime = default;
        task = Task.Run(Handle);
        return this;
    }

    public ProgressAccumulator Stop()
    {
        isRunning = false;
        return this;
    }

    public ProgressAccumulator Reset(double from = 0d, double to = 100d, double duration = 3000d, Action<double> handler = null!)
    {
        Stop();

        Current = From = from;
        To = to;
        Duration = duration;
        Handler = handler;
        return this;
    }

    private void Handle()
    {
        while (isRunning)
        {
            if (!SpinWait.SpinUntil(() => !isRunning, 50))
            {
                Calc();
                Handler?.Invoke(Current);
            }
        }
    }

    private double Calc()
    {
        if (durationTime <= Duration)
        {
            Current = (Animation ?? DoubleEasingAnimations.EaseOutCirc).Invoke(durationTime, From, To, Duration);
            durationTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
        }
        return Current;
    }
}
