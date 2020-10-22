using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Disfigure.GUI.Client.Views
{
    public class MessageView : UserControl
    {
        public MessageView() { InitializeComponent(); }

        private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }
    }
}
