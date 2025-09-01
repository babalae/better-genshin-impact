using BetterGenshinImpact.GameTask.GetGridIcons;
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GetGridIconsTests
{
    public class GridIconModelFixture
    {
        internal readonly Lazy<ModelLoader> modelLoader = new Lazy<ModelLoader>();
    }

    internal class ModelLoader
    {
        internal readonly InferenceSession session;
        internal readonly Dictionary<string, float[]> prototypes;
        public ModelLoader()
        {
            this.session = GridIconsAccuracyTestTask.LoadModel(out this.prototypes);
        }
    }
}
