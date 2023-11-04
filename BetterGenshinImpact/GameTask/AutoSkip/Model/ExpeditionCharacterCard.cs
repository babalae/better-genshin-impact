using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public class ExpeditionCharacterCard
{
    public string? Name { get; set; }

    public bool Idle { get; set; } = true;

    public string? Addition { get; set; }


    public List<Rect> Rects { get; set; } = new();

    //public ExpeditionCharacterCard(string name,string addition, bool idle)
    //{
    //    Name = name;
    //    Addition = addition;
    //    Idle = idle;
    //}
}