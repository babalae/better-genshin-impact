using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class GridScreenParams
    {
        internal int X1080p { get; private set; }
        internal int Y1080p { get; private set; }
        internal int Width1080p { get; private set; }
        internal int Height1080p { get; private set; }
        internal int Columns { get; private set; }
        internal int S1Round { get; private set; }
        internal int RoundMilliseconds { get; private set; }
        internal int S2Round { get; private set; }
        internal double S3Scale { get; private set; }

        internal Rect GetRect(ImageRegion gameScreen)
        {
            float scale = gameScreen.Height / 1080f;
            return new Rect((int)(scale * X1080p), (int)(scale * Y1080p), (int)(scale * Width1080p), (int)(scale * Height1080p));
        }

        private static readonly GridScreenParams weapons = new GridScreenParams()
        {
            X1080p = 106,
            Y1080p = 110,
            Width1080p = 1171,
            Height1080p = 845,
            Columns = 8,
            S1Round = 3,
            RoundMilliseconds = 40,
            S2Round = 32,
            S3Scale = 0.024
        };

        internal static FrozenDictionary<GridScreenName, GridScreenParams> Templates { get; } = new Dictionary<GridScreenName, GridScreenParams>() {
            { GridScreenName.Weapons, weapons },
            { GridScreenName.Artifacts, new GridScreenParams(){
                X1080p = 106,
                Y1080p = 162,
                Width1080p = 1171,
                Height1080p = 783,
                Columns = 8,
                S1Round = 3,
                RoundMilliseconds = 40,
                S2Round = 32,
                S3Scale = 0.024
            }},
            { GridScreenName.CharacterDevelopmentItems, weapons },
            { GridScreenName.Food, weapons },
            { GridScreenName.Materials, weapons },
            { GridScreenName.Gadget, weapons },
            { GridScreenName.Quest, weapons },
            { GridScreenName.PreciousItems, weapons },
            { GridScreenName.Furnishings, weapons },
            { GridScreenName.ArtifactSalvage, new GridScreenParams(){
                X1080p = 48,
                Y1080p = 106,
                Width1080p = 1267,
                Height1080p = 768,
                Columns = 9,
                S1Round = 3,
                RoundMilliseconds = 40,
                S2Round = 28,
                S3Scale = 0.018
            }}
        }.ToFrozenDictionary();
    }
}
