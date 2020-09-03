#region

using System.Collections.Generic;
using DisfigureCore.Message;

#endregion

namespace DisfigureCore
{
    public class Channel
    {
        private List<IMessage> _Messages;

        public string Name { get; }
        public IReadOnlyList<IMessage> Messages => _Messages;

        public Channel(string name, bool newChannel)
        {
            Name = name;
            // todo this'll use EF Core probably
            _Messages = new List<IMessage>();
        }

        public void CommitMessage(IMessage message)
        {
            // todo vet message to ensure it isn't fraudulent?
            _Messages.Add(message);
        }
    }
}
