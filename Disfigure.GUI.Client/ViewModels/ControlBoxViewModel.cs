#region

using ReactiveUI;
using System;
using System.Reactive;

#endregion

namespace Disfigure.GUI.Client.ViewModels
{
    public class ControlBoxViewModel : ViewModelBase
    {
        private string _Content;

        public string Content
        {
            get => _Content;
            set => this.RaiseAndSetIfChanged(ref _Content, value);
        }

        public ReactiveCommand<Unit, Unit> FlushContent { get; }

        public event EventHandler<string>? ContentFlushed;

        public ControlBoxViewModel()
        {
            _Content = string.Empty;

            FlushContent = ReactiveCommand.Create(OnMessageContentFlushed);
        }

        private void OnMessageContentFlushed()
        {
            if (!string.IsNullOrWhiteSpace(Content))
            {
                ContentFlushed?.Invoke(this, Content);
                Content = string.Empty;
            }
        }
    }
}