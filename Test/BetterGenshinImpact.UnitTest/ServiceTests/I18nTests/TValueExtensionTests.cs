using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.UnitTest.ServiceTests.I18nTests
{
    /// <summary>
    /// {i18n:TValue} 用于翻译数据绑定得到的值（例如中文字符串 ItemsSource 的每一项）。
    /// 两件事必须成立：默认语言下原样显示，切换语言后无需重建界面即可刷新。
    /// </summary>
    public class TValueExtensionTests
    {
        private const string Xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       xmlns:i18n="clr-namespace:BetterGenshinImpact.Service.I18n;assembly=BetterGI"
                       Text="{i18n:TValue}" />
            """;

        [Fact]
        public void TValue_DefaultLanguage_LeavesValueUnchanged()
        {
            var text = RunOnStaThread(() =>
            {
                I18nService.Instance.ChangeLanguage(I18nService.DefaultLanguage);
                var textBlock = (TextBlock)XamlReader.Parse(Xaml);
                textBlock.DataContext = "蒙德";
                Flush();
                return textBlock.Text;
            });

            // 中文用户看到的必须和改动前完全一样。
            Assert.Equal("蒙德", text);
        }

        [Fact]
        public void TValue_AfterLanguageChange_RefreshesWithoutRebuildingTheView()
        {
            var language = "zz-tvalue-test";
            var directory = Global.Absolute(Path.Combine("User", "I18n"));
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"{language}.json");
            File.WriteAllText(file, """{"蒙德":"Mondstadt"}""", Encoding.UTF8);

            try
            {
                var (before, after, restored) = RunOnStaThread(() =>
                {
                    I18nService.Instance.ChangeLanguage(I18nService.DefaultLanguage);
                    var textBlock = (TextBlock)XamlReader.Parse(Xaml);
                    textBlock.DataContext = "蒙德";
                    Flush();
                    var initial = textBlock.Text;

                    // 同一个已经创建好的元素：不重新解析 XAML，不重设 DataContext。
                    I18nService.Instance.ChangeLanguage(language);
                    Flush();
                    var translated = textBlock.Text;

                    I18nService.Instance.ChangeLanguage(I18nService.DefaultLanguage);
                    Flush();
                    return (initial, translated, textBlock.Text);
                });

                Assert.Equal("蒙德", before);
                Assert.Equal("Mondstadt", after);
                Assert.Equal("蒙德", restored);
            }
            finally
            {
                I18nService.Instance.ChangeLanguage(I18nService.DefaultLanguage);
                File.Delete(file);
            }
        }

        /// <summary>
        /// 让绑定引擎把待处理的更新跑完，否则刚设置的值还没写进目标属性。
        /// </summary>
        private static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        }

        private static T RunOnStaThread<T>(Func<T> action)
        {
            T result = default!;
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException("STA thread failed", failure);
            }

            return result;
        }
    }
}
