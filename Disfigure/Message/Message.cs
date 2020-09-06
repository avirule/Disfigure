using System;

namespace Disfigure.Message
{
    public abstract class Message<T> : IMessage<T>
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; }

        public abstract T Deserialize();
    }
}
