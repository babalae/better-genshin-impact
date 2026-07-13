using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest
{
    public class LocalizationFixture
    {
        private readonly ServiceProvider sp;
        public LocalizationFixture()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>()
                .AddLocalization();
            this.sp = services.BuildServiceProvider();
        }

        public IStringLocalizer<T> CreateStringLocalizer<T>()
        {
            return this.sp.GetRequiredService<IStringLocalizer<T>>();
        }
    }
}
