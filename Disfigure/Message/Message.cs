using System;

namespace Disfigure.Message
{
    public abstract class Message<T> : IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; }

        public Message(Guid author, DateTime utcTimestamp, byte[] content) => (Author, UtcTimestamp, Content) = (author, utcTimestamp, content);

        public abstract T Deserialize();
    }
}
