#region

using System;

#endregion

namespace Disfigure.Message
{
    public interface IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
    }
}
