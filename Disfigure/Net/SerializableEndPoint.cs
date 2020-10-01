#region

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

#endregion

namespace Disfigure.Net
{
    public readonly struct SerializableEndPoint
    {
        private const int _ADDRESS_FAMILY_OFFSET = 0;
        private const int _PORT_OFFSET = _ADDRESS_FAMILY_OFFSET + sizeof(ushort);
        private const int _IP_ADDRESS_OFFSET = _PORT_OFFSET + sizeof(ushort);

        /// <summary>
        ///     Port of <see cref="EndPoint" />.
        /// </summary>
        public ushort Port { get; }

        /// <summary>
        ///     <see cref="IPAddress" /> of <see cref="EndPoint" />.
        /// </summary>
        public IPAddress Address { get; }

        public SerializableEndPoint(IPAddress address, ushort port) => (Address, Port) = (address, port);

        public SerializableEndPoint(byte[] data)
        {
            Port = BitConverter.ToUInt16(data, _PORT_OFFSET);
            Address = new IPAddress(data[_IP_ADDRESS_OFFSET..]);
        }

        public byte[] Serialize()
        {
            byte[] addressBytes = Address.GetAddressBytes();
            byte[] data = new byte[_IP_ADDRESS_OFFSET + addressBytes.Length];

            Buffer.BlockCopy(BitConverter.GetBytes((ushort)Address.AddressFamily), 0, data, _ADDRESS_FAMILY_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(Port), 0, data, _PORT_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(addressBytes, 0, data, _IP_ADDRESS_OFFSET, addressBytes.Length);

            return data;
        }

        public override string ToString()
        {
            string format = Address.AddressFamily == AddressFamily.InterNetworkV6 ? "[{0}]:{1}" : "{0}:{1}";
            return string.Format(format, Address, Port.ToString(NumberFormatInfo.InvariantInfo));
        }

        public static explicit operator IPEndPoint(SerializableEndPoint serializableEndPoint) =>
            new IPEndPoint(serializableEndPoint.Address, serializableEndPoint.Port);
    }
}
