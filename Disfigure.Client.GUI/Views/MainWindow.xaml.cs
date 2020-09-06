using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Disfigure.Client.GUI.Views
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
