using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {

        public MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            DataContext = ViewModel = new();
            InitializeComponent();
        }

    }
}