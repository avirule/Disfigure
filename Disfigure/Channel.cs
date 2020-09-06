#region

using System;
using System.Collections.Generic;
using System.Text;
using Disfigure.Message;

#endregion

namespace Disfigure
{
    public class Channel
    {
        private readonly List<IMessage> _Messages;

        public Guid Guid { get; }
        public string Name { get; }
        public IReadOnlyList<IMessage> Messages => _Messages;

        public Channel(Guid guid, string name)
        {
            Guid = guid;
            Name = name;

            // todo this'll use EF Core probably
            _Messages = new List<IMessage>();
        }

        public void CommitMessage(IMessage message)
        {
            // todo vet message to ensure it isn't fraudulent?
            _Messages.Add(message);
        }

        public unsafe byte[] Serialize()
        {
            byte[] name = Encoding.Unicode.GetBytes(Name);
            byte[] serialized = new byte[sizeof(Guid) + name.Length];

            Buffer.BlockCopy(Guid.ToByteArray(), 0, serialized, 0, sizeof(Guid));
            Buffer.BlockCopy(name, 0, serialized, sizeof(Guid), name.Length);

            return serialized;
        }
    }
}
