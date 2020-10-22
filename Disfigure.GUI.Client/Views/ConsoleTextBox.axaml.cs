using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Disfigure.GUI.Client.Views
{
    public class ConsoleTextBox : UserControl
    {
        public ConsoleTextBox() { InitializeComponent(); }

        private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
    }
}
