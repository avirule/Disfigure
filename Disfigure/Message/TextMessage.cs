#region

using System;

#endregion

namespace Disfigure.Message
{
    public class TextMessage : IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public string Content { get; }
    }
}
