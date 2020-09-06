#region

using System;

#endregion

namespace Disfigure.Message
{
    public class ImageMessage : IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] CompressedBytes { get; }
    }
}
