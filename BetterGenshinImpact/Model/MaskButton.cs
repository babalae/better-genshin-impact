using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;

namespace BetterGenshinImpact.Model
{
    public class MaskButton
    {
        public string Name { get; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public IRelayCommand ClickAction { get; set; }


        public MaskButton(string name, Rect rect, Action clickAction)
        {
            Name = name;
            var scale = TaskContext.Instance().DpiScale;
            X = rect.X / scale;
            Y = rect.Y / scale;
            Width = rect.Width / scale;
            Height = rect.Height / scale;
            ClickAction = new RelayCommand(clickAction);
        }

        public override bool Equals(object? obj)
        {
            if (obj is MaskButton button)
            {
                return Name == button.Name;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}