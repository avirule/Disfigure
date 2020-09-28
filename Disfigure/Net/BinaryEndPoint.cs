#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

#endregion

namespace Disfigure.Net
{
    public class BinaryEndPoint
    {
        private const int _ADDRESS_FAMILY_OFFSET = 0;
        private const int _PORT_OFFSET = _ADDRESS_FAMILY_OFFSET + sizeof(ushort);
        private const int _IP_ADDRESS_OFFSET = _PORT_OFFSET + sizeof(ushort);

        private readonly byte[] _Data;

        public ReadOnlyMemory<byte> Data => _Data;
        public AddressFamily AddressFamily => (AddressFamily)BitConverter.ToUInt16(_Data);
        public ushort Port => BitConverter.ToUInt16(_Data, sizeof(ushort));
        public IPAddress Address => new IPAddress(_Data[4..]);

        public BinaryEndPoint(IPEndPoint ipEndPoint)
        {
            byte[] addressBytes = ipEndPoint.Address.GetAddressBytes();

            _Data = new byte[sizeof(ushort) + sizeof(ushort) + addressBytes.Length];
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)ipEndPoint.AddressFamily), 0, _Data, _ADDRESS_FAMILY_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(ipEndPoint.Port), 0, _Data, _PORT_OFFSET, sizeof(ushort));
            Buffer.BlockCopy(addressBytes, 0, _Data, _IP_ADDRESS_OFFSET, addressBytes.Length);
        }

        public BinaryEndPoint(byte[] data) => _Data = data;

        public static explicit operator IPEndPoint(BinaryEndPoint binaryEndPoint) => new IPEndPoint(binaryEndPoint.Address, binaryEndPoint.Port);
    }
}
