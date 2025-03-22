using BetterGenshinImpact.GameTask.AutoFishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        private readonly IStringLocalizer<AutoFishingImageRecognition>? autoFishingImageRecognitionStringLocalizer;
        public BehavioursTests()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging().AddLocalization();
            using ServiceProvider sp = services.BuildServiceProvider();
            this.autoFishingImageRecognitionStringLocalizer = sp.GetRequiredService<IStringLocalizer<AutoFishingImageRecognition>>();
        }
    }
}
