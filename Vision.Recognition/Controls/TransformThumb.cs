using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows;

namespace Vision.Recognition.Controls
{
    public class TransformThumb : Control
    {
        static TransformThumb()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TransformThumb), new FrameworkPropertyMetadata(typeof(TransformThumb)));
        }

        /// <summary>
        /// 内容
        /// </summary>
        public object Content
        {
            get { return (object)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }
        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(object), typeof(TransformThumb), new PropertyMetadata(null));


        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this);
            if (adornerLayer != null)
            {
                ElementAdorner adorner = new ElementAdorner(this);
                adornerLayer.Add(adorner);
            }
        }
    }
}
