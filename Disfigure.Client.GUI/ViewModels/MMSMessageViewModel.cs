using Disfigure.Message;

namespace Disfigure.Client.GUI.ViewModels
{
    public class MMSMessageViewModel
    {
        public IMessage Message { get; }

        public MMSMessageViewModel(IMessage message) => Message = message;
    }
}
