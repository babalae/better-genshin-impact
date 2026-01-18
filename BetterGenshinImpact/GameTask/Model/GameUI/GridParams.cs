using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class GridParams
    {
        internal Rect Roi { get; private set; }
        internal int Columns { get; private set; }
        internal int S1Round { get; private set; }
        internal int RoundMilliseconds { get; private set; }
        internal int S2Round { get; private set; }
        internal double S3Scale { get; private set; }

        public GridParams(Rect roi1080p, int columns, int s1Round, int roundMilliseconds, int s2Round, double s3Scale)
        {
            Roi = roi1080p.Multiply(TaskContext.Instance().SystemInfo.AssetScale);
            Columns = columns;
            S1Round = s1Round;
            RoundMilliseconds = roundMilliseconds;
            S2Round = s2Round;
            S3Scale = s3Scale;
        }

        private static readonly GridParams weapons = new GridParams(new Rect(106, 110, 1171, 845), 8, 3, 40, 32, 0.024);

        internal static FrozenDictionary<GridScreenName, GridParams> Templates { get; } = new Dictionary<GridScreenName, GridParams>() {
            { GridScreenName.Weapons, weapons },
            { GridScreenName.Artifacts, new GridParams(new Rect(106, 162, 1171, 783), 8, 3, 40, 32, 0.024)},
            { GridScreenName.CharacterDevelopmentItems, weapons },
            { GridScreenName.Food, weapons },
            { GridScreenName.Materials, weapons },
            { GridScreenName.Gadget, weapons },
            { GridScreenName.Quest, weapons },
            { GridScreenName.PreciousItems, weapons },
            { GridScreenName.Furnishings, weapons },
            { GridScreenName.ArtifactSalvage, new GridParams(new Rect(48, 106, 1267, 768), 9, 3, 40, 28, 0.018)}
        }.ToFrozenDictionary();
    }
}
