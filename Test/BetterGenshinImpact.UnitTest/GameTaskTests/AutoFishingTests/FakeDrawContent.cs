using BetterGenshinImpact.View.Drawable;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    internal class FakeDrawContent : DrawContent
    {
        public override void ClearAll()
        {
        }

        public override void PutLine(string key, LineDrawable newLine)
        {
        }

        public override void PutOrRemoveRectList(string key, List<RectDrawable>? list)
        {
        }

        public override void PutRect(string key, RectDrawable newRect)
        {
        }

        public override void RemoveLine(string key)
        {
        }

        public override void RemoveRect(string key)
        {
        }
    }
}
