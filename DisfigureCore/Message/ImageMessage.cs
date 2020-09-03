using System;

namespace DisfigureCore.Message
{
    public class ImageMessage : IMessage
    {
        public Guid Author { get; }
        public DateTime UtcTimestamp { get; }
        public byte[] CompressedBytes { get; }
    }
}