using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace BetterGenshinImpact.View.Pages.TaskSettings
{
    /// <summary>
    /// Interaction logic for AutoTrackTaskControl.xaml
    /// Auto track task control module (visible in debug mode)
    /// </summary>
    public partial class AutoTrackTaskControl : UserControl
    {
        public AutoTrackTaskControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handle hyperlink navigation request
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Open link in default browser
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}