using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model
{
    [Serializable]
    public class PathingTask
    {
        public PathingTaskInfo? Info { get; set; }
        public List<Waypoint> Waypoints { get; set; } = [];
    }
}
