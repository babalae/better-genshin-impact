using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>仅圆形区域参与命中测试的透明遮罩，圈外仍由原界面处理。</summary>
public sealed class AccountRecordsCloseGuardAdorner : Adorner
{
    /// <summary>保护半径，单位 DIP。</summary>
    private const double Radius = 20;
    /// <summary>首次按下后移除遮罩的回调。</summary>
    private readonly Action _dismiss;
    /// <summary>关闭前按钮中心，采用被装饰视图的坐标。</summary>
    public Point Center { get; }

    /// <summary>预建时不阻挡关闭按钮本身，待 Click 时启用命中。</summary>
    public AccountRecordsCloseGuardAdorner(UIElement host, Point center, Action dismiss) : base(host)
    {
        Center = center;
        _dismiss = dismiss;
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>按圆形边界判断，不拦截整个装饰层矩形。</summary>
    public bool Contains(Point point) => (point - Center).LengthSquared <= Radius * Radius;

    /// <summary>绘制透明命中区域，不遮挡内容。</summary>
    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawEllipse(Brushes.Transparent, null, Center, Radius, Radius);

    /// <summary>只有圆内命中，圈外返回下层控件。</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    /// <summary>消费下一次按下后解除；事件源是遮罩，不再路由到下方状态灯。</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dismiss();
    }

    /// <summary>关闭手势的抬起只消费，不解除保护。</summary>
    protected override void OnMouseUp(MouseButtonEventArgs e) => e.Handled = true;
}
