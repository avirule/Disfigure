#region

using System.Net;
using CommandLine;

#endregion

namespace Disfigure.CLI
{
    public class HostModuleOption : LogLevelOption
    {
        private string? _IPAddressUnparsed;

        [Value(0, MetaName = nameof(IPAddressUnparsed), Required = true, Default = "127.0.0.1")]
        public string IPAddressUnparsed
        {
            get => _IPAddressUnparsed ?? string.Empty;
            set
            {
                _IPAddressUnparsed = value;
                IPAddress = IPAddress.Parse(_IPAddressUnparsed);
            }
        }

        [Value(1, MetaName = nameof(Port), Required = true, Default = 8998)]
        public ushort Port { get; set; }

        public IPAddress IPAddress { get; private set; } = null!;
    }
}
