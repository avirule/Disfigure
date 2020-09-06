#region

using System;

#endregion

namespace Disfigure.Message
{
    public interface IMessage<out T>
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] Content { get; }

        public T Deserialize();
    }
}
