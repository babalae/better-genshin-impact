using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class Waypoint
{




    //异常识别处理
    public class Misidentification
    {

        //何时处理   pathTooFar  路径过远  Unrecognized 未识别
        public List<string> Type { get; set; }= ["unrecognized"];
        //处理方式  previousDetectedPoint  取上一个识别到的点位置  ,  mapRecognition 大地图识别 , ScheduledArrival  特定时间到达
        public string HandlingMode { get; set; } = "previousDetectedPoint";
        //自行预估的时间
        public int ArrivalTime { get; set; } = 0;
    }
    public class ExtParams
    {
        public Misidentification Misidentification { get; set; } = new();
        public string Description { get; set; } = "";
        //normal 小怪,elite 精英,legendary 传奇
        public string MonsterTag { get; set; }
    }



    public double X { get; set; }
    public double Y { get; set; }
    
    public ExtParams PointExtParams { get; set; }=new ExtParams();

    /// <summary>
    /// <see cref="WaypointType"/>
    /// </summary>
    public string Type { get; set; } = WaypointType.Path.Code;

    /// <summary>
    /// <see cref="MoveModeEnum"/>
    /// </summary>
    public string MoveMode { get; set; } = MoveModeEnum.Walk.Code;

    /// <summary>
    /// <see cref="ActionEnum"/>
    /// </summary>
    public string? Action { get; set; }
    
    public string? ActionParams { get; set; }
}
