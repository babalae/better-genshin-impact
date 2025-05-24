using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest
{
    [CollectionDefinition("Init Collection")]
    public class InitCollection : ICollectionFixture<PaddleFixture>, ICollectionFixture<TorchFixture>
    {
    }
}
