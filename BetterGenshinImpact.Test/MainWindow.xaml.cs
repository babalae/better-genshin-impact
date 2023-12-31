using System.Windows;

namespace BetterGenshinImpact.Test
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {

            InitializeComponent();
            
            new HsvTestWindow().Run();
          
        }
    }
}