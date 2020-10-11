#region

using CommandLine;
using Serilog.Events;
using System.Net;

#endregion

namespace Disfigure.CLI
{
    [Verb(nameof(Host), true)]
    public class Host
    {
        private string? _IPAddressUnparsed;

        [Option('l', "loglevel", SetName = nameof(LogLevel), Required = false, Default = LogEventLevel.Information)]
        public LogEventLevel LogLevel { get; set; }

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

        [Value(2, MetaName = nameof(Name), Required = false)]
        public string? Name { get; set; }

        public IPAddress IPAddress { get; private set; } = null!;
    }
}