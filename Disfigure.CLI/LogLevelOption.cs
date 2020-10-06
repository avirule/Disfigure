#region

using CommandLine;
using Serilog.Events;

#endregion

namespace Disfigure.CLI
{
    public class LogLevelOption
    {
        [Option('l', "loglevel", SetName = nameof(LogLevel), Required = false, Default = LogEventLevel.Information)]
        public LogEventLevel LogLevel { get; set; }
    }
}
