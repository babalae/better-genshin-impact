using BetterGenshinImpact.Core.Recognition.OCR;
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
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private static IOcrService ocrService;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        public BehavioursTests()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging().AddLocalization();
            using ServiceProvider sp = services.BuildServiceProvider();
            this.autoFishingImageRecognitionStringLocalizer = sp.GetRequiredService<IStringLocalizer<AutoFishingImageRecognition>>();

            LazyInitializer.EnsureInitialized(ref ocrService, () => new PaddleOcrService());
        }
    }
}
