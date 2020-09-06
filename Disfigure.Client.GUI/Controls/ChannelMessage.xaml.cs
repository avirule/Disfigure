using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Disfigure.Client.GUI.Controls
{
    public class ChannelMessage : UserControl
    {
        public ChannelMessage()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}