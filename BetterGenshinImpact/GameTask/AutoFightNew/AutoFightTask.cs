using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using OpenCvSharp;
using BetterGenshinImpact.Assets.Model.DepthAnything;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Pen = System.Drawing.Pen;

namespace BetterGenshinImpact.GameTask.AutoFightNew;

public class AutoFightTask
{
    private readonly BgiYoloPredictor _predictor = BgiOnnxFactory.Instance.CreateYoloPredictor(BgiOnnxModel.BgiEnemy);
    
    private List<Point2f> GetEnemyPos(ImageRegion img)
    {
        var result = _predictor.Detect(img);
        var list = new List<RectDrawable>();
        var ret = new List<Point2f>();
        foreach (var box in result["enemy"])
        {
            list.Add(img.ToRectDrawable(box, "EnemyHealthBar"));
            ret.Add(new Point2f((box.Left + box.Right)/2f, (box.Top + box.Bottom)/2f));
        }

        VisionContext.Instance().DrawContent.PutOrRemoveRectList("EnemyHealthBar", list);

        return ret;
    }

    public async Task FaceToEnemy(CancellationToken ct, int maxMilliseconds=5000)
    {
        var start = DateTime.UtcNow;
        var ratio = 5f;
        var previousDistance = 0;
        while (!ct.IsCancellationRequested && (DateTime.UtcNow - start).TotalMilliseconds < maxMilliseconds)
        {
            var screen = CaptureToRectArea();
            var enemies = GetEnemyPos(screen);
            var cx = screen.Width / 2;
            var cy = screen.Height / 2;
            var minDx = float.MaxValue;
            var minDy = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                var x = enemy.X;
                var y = enemy.Y;
                var dx = Math.Abs(x - cx);
                var dy = Math.Abs(y - cy);
                if (Math.Sqrt(minDx * minDx + minDy * minDy) < Math.Sqrt(dx * dx + dy * dy))
                {
                    minDx = dx;
                    minDy = dy;
                }
            }

            var distance = Math.Sqrt(minDx * minDx + minDy * minDy);
            if (distance < 20)
                return;
            if (distance > previousDistance)
                ratio -= 1;
            if (ratio <= 0)
                ratio = 1;
            
            minDx = minDx/minDx * 10 * ratio;
            minDy = minDy/minDy * 10 * ratio;
            Simulation.MouseEvent.Move((int)minDx, (int)minDy);
        }
    }
}