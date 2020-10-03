#region

using System;
using CommandLine;
using Serilog.Events;

#endregion

namespace Disfigure.CLI
{
    public class ModuleOption
    {
        [Option('l', "loglevel", SetName = nameof(LogEventLevel), Required = false, Default = LogEventLevel.Information)]
        public LogEventLevel LogEventLevel { get; set; }
    }

    public class ServerModuleOption : ModuleOption
    {
        private string _IPAddress = String.Empty;

        [Value(0, MetaName = nameof(IPAddress), Required = true)]
        public string IPAddress
        {
            get => _IPAddress;
            set
            {
                string lower = value.ToLowerInvariant();
                _IPAddress = lower switch
                {
                    "localhost" => "127.0.0.1",
                    _ => lower
                };
            }
        }

        [Value(1, MetaName = nameof(Port), Required = true)]
        public ushort Port { get; set; }
    }
}
