#region

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

#endregion

namespace Disfigure.Net
{
    public readonly struct SerializableEndPoint
    {
        /// <summary>
        ///     Port of <see cref="EndPoint" />.
        /// </summary>
        public readonly ushort Port;

        /// <summary>
        ///     <see cref="IPAddress" /> of <see cref="EndPoint" />.
        /// </summary>
        public readonly IPAddress Address;

        public SerializableEndPoint(IPAddress address, ushort port) => (Address, Port) = (address, port);

        public SerializableEndPoint(ReadOnlySpan<byte> data)
        {
            Port = MemoryMarshal.Read<ushort>(data.Slice(0));
            Address = new IPAddress(data.Slice(sizeof(ushort)));
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
            byte[] data = new byte[sizeof(ushort) + addressBytes.Length];

            Buffer.BlockCopy(BitConverter.GetBytes(Port), 0, data, 0, sizeof(ushort));
            Buffer.BlockCopy(addressBytes, 0, data, sizeof(ushort), addressBytes.Length);

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