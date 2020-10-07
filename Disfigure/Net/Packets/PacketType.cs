namespace Disfigure.Net.Packets
{
    public enum PacketType : byte
    {
        EncryptionKeys,
        Connect,
        Disconnect,
        Connected,
        Disconnected,
        Ping,
        Pong,
        Text,
        Sound,
        Media,
        Video,
        Administration,
        Operation,
        Identity,
        ChannelIdentity,
    }
}
