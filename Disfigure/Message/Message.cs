#region

using System;

#endregion

namespace Disfigure.Message
{
    public abstract class Message<T> : IMessage
    {
        public Message(Guid author, DateTime utcTimestamp, byte[] content) => (Author, UtcTimestamp, Content) = (author, utcTimestamp, content);

        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; }

        public abstract T Deserialize();
    }
}
