using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Disfigure.GUI.Client.Views
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            // HasSystemDecorations = false;
        }

        private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
    }
}
