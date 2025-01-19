using System;
using System.Threading.Tasks;
using BetterGenshinImpact.ViewModel.Pages;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Pages
{
    public partial class HomePage
    {
        public HomePageViewModel ViewModel { get; }

        public HomePage(HomePageViewModel viewModel, HotKeyPageViewModel hotKeyPageViewModel)
        {
            DataContext = ViewModel = viewModel;
            InitializeComponent();

            // hotKeyPageViewModel 放在这里是为了在首页就初始化热键

            Task.Run(async () =>
            {
                bool first = true;
                // 显示当前时间
                while (true)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (first)
                        {
                            first = false;
                            ClockStartBlock.Text = DateTime.Now.ToString("HH:mm:ss.fff") + " | " + Kernel32.GetTickCount().ToString();
                        }

                        ClockBlock.Text = DateTime.Now.ToString("HH:mm:ss.fff");
                        TickBlock.Text = Kernel32.GetTickCount().ToString();
                    });
                    await Task.Delay(10);
                }
            });
        }
    }
}