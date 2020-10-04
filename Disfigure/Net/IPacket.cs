namespace Disfigure.Net
{
    public interface IPacket<out TPacket> where TPacket : IPacket<TPacket>
    {
        public byte[] Serialize();

        public string ToString();
    }
}
