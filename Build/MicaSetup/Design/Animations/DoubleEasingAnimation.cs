using System;

namespace MicaSetup.Controls.Animations;

public delegate double DoubleEasingAnimation(double t, double b, double c, double d);

public static class DoubleEasingAnimations
{
    public static double EaseInOutQuad(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d / 2d) < 1d)
        {
            return c / 2d * t * t + b;
        }
        return -c / 2d * ((t -= 1d) * (t - 2d) - 1d) + b;
    }

    public static double EaseInQuad(double t, double b, double c, double d)
    {
        c -= b;
        return c * (t /= d) * t + b;
    }

    public static double EaseOutQuad(double t, double b, double c, double d)
    {
        c -= b;
        return -c * (t /= d) * (t - 2d) + b;
    }

    public static double EaseInCubic(double t, double b, double c, double d)
    {
        c -= b;
        return c * (t /= d) * t * t + b;
    }

    public static double EaseOutCubic(double t, double b, double c, double d)
    {
        c -= b;
        return c * ((t = t / d - 1d) * t * t + 1d) + b;
    }

    public static double EaseInOutCubic(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d / 2d) < 1d)
        {
            return c / 2d * t * t * t + b;
        }
        return c / 2d * ((t -= 2d) * t * t + 2d) + b;
    }

    public static double EaseInQuart(double t, double b, double c, double d)
    {
        c -= b;
        return c * (t /= d) * t * t * t + b;
    }

    public static double EaseOutQuart(double t, double b, double c, double d)
    {
        c -= b;
        return -c * ((t = t / d - 1d) * t * t * t - 1d) + b;
    }

    public static double EaseInOutQuart(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d / 2d) < 1d)
        {
            return c / 2d * t * t * t * t + b;
        }
        return -c / 2d * ((t -= 2d) * t * t * t - 2d) + b;
    }

    public static double EaseInQuint(double t, double b, double c, double d)
    {
        c -= b;
        return c * (t /= d) * t * t * t * t + b;
    }

    public static double EaseOutQuint(double t, double b, double c, double d)
    {
        c -= b;
        return c * ((t = t / d - 1d) * t * t * t * t + 1d) + b;
    }

    public static double EaseInOutQuint(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d / 2d) < 1d)
        {
            return c / 2d * t * t * t * t * t + b;
        }
        return c / 2d * ((t -= 2d) * t * t * t * t + 2d) + b;
    }

    public static double EaseInSine(double t, double b, double c, double d)
    {
        c -= b;
        return -c * Math.Cos(t / d * 1.5707963267948966d) + c + b;
    }

    public static double EaseOutSine(double t, double b, double c, double d)
    {
        c -= b;
        return c * Math.Sin(t / d * 1.5707963267948966d) + b;
    }

    public static double EaseInOutSine(double t, double b, double c, double d)
    {
        c -= b;
        return -c / 2d * (Math.Cos(3.141592653589793d * t / d) - 1d) + b;
    }

    public static double EaseInExpo(double t, double b, double c, double d)
    {
        c -= b;
        if (t != 0d)
        {
            return c * Math.Pow(2d, 10d * (t / d - 1d)) + b;
        }
        return b;
    }

    public static double EaseOutExpo(double t, double b, double c, double d)
    {
        c -= b;
        if (t != d)
        {
            return c * (-Math.Pow(2d, -10d * t / d) + 1d) + b;
        }
        return b + c;
    }

    public static double EaseInOutExpo(double t, double b, double c, double d)
    {
        c -= b;
        if (t == 0d)
        {
            return b;
        }
        if (t == d)
        {
            return b + c;
        }
        if ((t /= d / 2d) < 1d)
        {
            return c / 2d * Math.Pow(2d, 10d * (t - 1d)) + b;
        }
        return c / 2d * (-Math.Pow(2d, -10d * (t -= 1d)) + 2d) + b;
    }

    public static double EaseInCirc(double t, double b, double c, double d)
    {
        c -= b;
        return -c * (Math.Sqrt(1d - (t /= d) * t) - 1d) + b;
    }

    public static double EaseOutCirc(double t, double b, double c, double d)
    {
        c -= b;
        return c * Math.Sqrt(1d - (t = t / d - 1d) * t) + b;
    }

    public static double EaseInOutCirc(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d / 2d) < 1d)
        {
            return -c / 2d * (Math.Sqrt(1d - t * t) - 1d) + b;
        }
        return c / 2d * (Math.Sqrt(1d - (t -= 2d) * t) + 1d) + b;
    }

    public static double EaseInElastic(double t, double b, double c, double d)
    {
        c -= b;
        double num = 0d;
        double num2 = c;
        if (t == 0d)
        {
            return b;
        }
        if ((t /= d) == 1d)
        {
            return b + c;
        }
        if (num == 0d)
        {
            num = d * 0.3d;
        }
        double num3;
        if (num2 < Math.Abs(c))
        {
            num2 = c;
            num3 = num / 4d;
        }
        else
        {
            num3 = num / 6.283185307179586d * Math.Asin(c / num2);
        }
        return -(num2 * Math.Pow(2d, 10d * (t -= 1d)) * Math.Sin((t * d - num3) * 6.283185307179586d / num)) + b;
    }

    public static double EaseOutElastic(double t, double b, double c, double d)
    {
        c -= b;
        double num = 0d;
        double num2 = c;
        if (t == 0d)
        {
            return b;
        }
        if ((t /= d) == 1d)
        {
            return b + c;
        }
        if (num == 0d)
        {
            num = d * 0.3d;
        }
        double num3;
        if (num2 < Math.Abs(c))
        {
            num2 = c;
            num3 = num / 4d;
        }
        else
        {
            num3 = num / 6.283185307179586d * Math.Asin(c / num2);
        }
        return num2 * Math.Pow(2d, -10d * t) * Math.Sin((t * d - num3) * 6.283185307179586d / num) + c + b;
    }

    public static double EaseInOutElastic(double t, double b, double c, double d)
    {
        c -= b;
        double num = 0d;
        double num2 = c;
        if (t == 0d)
        {
            return b;
        }
        if ((t /= d / 2d) == 2d)
        {
            return b + c;
        }
        if (num == 0d)
        {
            num = d * 0.44999999999999996d;
        }
        double num3;
        if (num2 < Math.Abs(c))
        {
            num2 = c;
            num3 = num / 4d;
        }
        else
        {
            num3 = num / 6.283185307179586d * Math.Asin(c / num2);
        }
        if (t < 1d)
        {
            return -0.5d * (num2 * Math.Pow(2d, 10d * (t -= 1d)) * Math.Sin((t * d - num3) * 6.283185307179586d / num)) + b;
        }
        return num2 * Math.Pow(2d, -10d * (t -= 1d)) * Math.Sin((t * d - num3) * 6.283185307179586d / num) * 0.5d + c + b;
    }

    public static double EaseInBounce(double t, double b, double c, double d)
    {
        c -= b;
        return c - EaseOutBounce(d - t, 0d, c, d) + b;
    }

    public static double EaseOutBounce(double t, double b, double c, double d)
    {
        c -= b;
        if ((t /= d) < 0.36363636363636365d)
        {
            return c * (7.5625d * t * t) + b;
        }
        if (t < 0.7272727272727273d)
        {
            return c * (7.5625d * (t -= 0.5454545454545454d) * t + 0.75d) + b;
        }
        if (t < 0.9090909090909091d)
        {
            return c * (7.5625d * (t -= 0.8181818181818182d) * t + 0.9375d) + b;
        }
        return c * (7.5625d * (t -= 0.9545454545454546d) * t + 0.984375d) + b;
    }

    public static double EaseInOutBounce(double t, double b, double c, double d)
    {
        c -= b;
        if (t < d / 2d)
        {
            return EaseInBounce(t * 2d, 0d, c, d) * 0.5d + b;
        }
        return EaseOutBounce(t * 2d - d, 0d, c, d) * 0.5d + c * 0.5d + b;
    }

    public static double Linear(double t, double b, double c, double d)
    {
        c -= b;
        return t / d * c + b;
    }

    public static DoubleEasingAnimation[] Function { get; } = new DoubleEasingAnimation[]
    {
        EaseInOutQuad,
        EaseInQuad,
        EaseOutQuad,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
        EaseInQuart,
        EaseOutQuart,
        EaseInOutQuart,
        EaseInQuint,
        EaseOutQuint,
        EaseInOutQuint,
        EaseInSine,
        EaseOutSine,
        EaseInOutSine,
        EaseInExpo,
        EaseOutExpo,
        EaseInOutExpo,
        EaseInCirc,
        EaseOutCirc,
        EaseInOutCirc,
        EaseInElastic,
        EaseOutElastic,
        EaseInOutElastic,
        EaseInBounce,
        EaseOutBounce,
        EaseInOutBounce,
        Linear,
    };
}

public enum DoubleEasingAnimationType
{
    InOutQuad,
    InQuad,
    OutQuad,
    InCubic,
    OutCubic,
    InOutCubic,
    InQuart,
    OutQuart,
    InOutQuart,
    InQuint,
    OutQuint,
    InOutQuint,
    InSine,
    OutSine,
    InOutSine,
    InExpo,
    OutExpo,
    InOutExpo,
    InCirc,
    OutCirc,
    InOutCirc,
    InElastic,
    OutElastic,
    InOutElastic,
    InBounce,
    OutBounce,
    InOutBounce,
    Linear,
}
