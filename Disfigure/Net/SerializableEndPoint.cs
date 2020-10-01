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

        /// <summary>
        ///     Converts <see cref="SerializableEndPoint" /> to a byte array.
        /// </summary>
        /// <remarks>
        ///     Serialized byte layout is as follows [PropertyName (bytes)]:
        ///     AddressFamily (2) | Port (2) | Address (length - 4)
        /// </remarks>
        /// <returns>
        ///     Byte array of serialized end point.
        /// </returns>
        public byte[] Serialize()
        {
            byte[] addressBytes = Address.GetAddressBytes();
            byte[] data = new byte[_IP_ADDRESS_OFFSET + addressBytes.Length];

            Buffer.BlockCopy(BitConverter.GetBytes((ushort)Address.AddressFamily), 0, data, _ADDRESS_FAMILY_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(Port), 0, data, _PORT_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(addressBytes, 0, data, _IP_ADDRESS_OFFSET, addressBytes.Length);

            return data;
        }

        /// <summary>
        ///     Converts <see cref="SerializableEndPoint" /> to a <see cref="string" />.
        /// </summary>
        /// <remarks>
        ///     This function returns in the same format as an <see cref="IPEndPoint" />.
        /// </remarks>
        /// <returns>
        ///     <see cref="string" /> representation of <see cref="SerializableEndPoint" />.
        /// </returns>
        public override string ToString()
        {
            string format = Address.AddressFamily == AddressFamily.InterNetworkV6 ? "[{0}]:{1}" : "{0}:{1}";
            return string.Format(format, Address, Port.ToString(NumberFormatInfo.InvariantInfo));
        }

        public static explicit operator IPEndPoint(SerializableEndPoint serializableEndPoint) =>
            new IPEndPoint(serializableEndPoint.Address, serializableEndPoint.Port);
    }
}
