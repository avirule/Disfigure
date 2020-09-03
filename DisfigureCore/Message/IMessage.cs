using System;

namespace DisfigureCore.Message
{
    public interface IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
    }
}