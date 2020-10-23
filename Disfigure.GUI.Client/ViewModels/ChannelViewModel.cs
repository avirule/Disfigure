#region

using System;
using System.Collections.ObjectModel;
using ReactiveUI;

#endregion


namespace Disfigure.GUI.Client.ViewModels
{
    public class ChannelViewModel : ViewModelBase
    {
        private int _SelectedMessageIndex;

        public Guid Identity { get; }
        public string FriendlyName { get; }
        public ObservableCollection<MessageViewModel> Messages { get; }
        public int SelectedMessageIndex { get => _SelectedMessageIndex; set => this.RaiseAndSetIfChanged(ref _SelectedMessageIndex, value); }

        public ChannelViewModel(Guid identity, string friendlyName)
        {
            Identity = identity;
            FriendlyName = friendlyName;
            Messages = new ObservableCollection<MessageViewModel>();
        }
    }
}
