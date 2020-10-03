#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Serilog;
using Serilog.Events;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

#endregion

namespace Disfigure.Modules
{
    public class ServerModuleConfiguration
    {
        private const string _LOGGING_TABLE = "logging";
        private const string _LOG_LEVEL_VALUE = "loglevel";

        private const string _HOSTING_TABLE = "hosting";
        private const string _IP_ADDRESS_VALUE = "ipaddress";
        private const string _PORT_VALUE = "port";

        private DocumentSyntax? _DocumentSyntax;
        private TomlTable? _DocumentTable;
        private TomlTable? _LoggingTable;
        private TomlTable? _HostingTable;

        public LogEventLevel LogLevel => (LogEventLevel)Convert.ToInt32((long)_LoggingTable[_LOG_LEVEL_VALUE]);

        public IPAddress HostingIPAddress => IPAddress.Parse((string)_HostingTable[_IP_ADDRESS_VALUE]);
        public ushort HostingPort => Convert.ToUInt16((long)_HostingTable[_PORT_VALUE]);

        public ServerModuleConfiguration(string configurationName, bool executeInPlaceMode)
        {
            string configurationDirectoryPath = executeInPlaceMode
                ? @"./Configurations/"
                : $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/Disfigure/Configurations";

            string configurationFilePath = Path.Combine(configurationDirectoryPath, $"{configurationName}.toml");

            ValidateFileStructure(configurationDirectoryPath, configurationFilePath);
            LoadDocumentSyntax(configurationFilePath);
        }

        private static void ValidateFileStructure(string configurationDirectoryPath, string configurationFilePath)
        {
            if (!Directory.Exists(configurationDirectoryPath))
            {
                Log.Information("Configuration directory not found, creating...");
                Directory.CreateDirectory(configurationDirectoryPath);
            }

            if (!File.Exists(configurationFilePath))
            {
                Log.Information("Configuration file not found, creating...");
                CreateConfigurationFile(configurationFilePath);
            }
        }

        private static void CreateConfigurationFile(string configurationFilePath)
        {
            using FileStream fileStream = File.Create(configurationFilePath);

            DocumentSyntax documentSyntax = new DocumentSyntax
            {
                Tables =
                {
                    new TableSyntax(_LOGGING_TABLE)
                    {
                        Items =
                        {
                            { _LOG_LEVEL_VALUE, (int)LogEventLevel.Information }
                        }
                    },
                    new TableSyntax(_HOSTING_TABLE)
                    {
                        Items =
                        {
                            { _IP_ADDRESS_VALUE, IPAddress.Loopback.ToString() },
                            { _PORT_VALUE, 8898 }
                        }
                    }
                }
            };

            // add new lines after tables
            foreach (TableSyntax? tableSyntax in documentSyntax.Tables.Cast<TableSyntax?>())
            {
                if (tableSyntax is null)
                {
                    continue;
                }

                tableSyntax.TrailingTrivia = new List<SyntaxTrivia>
                {
                    SyntaxFactory.NewLineTrivia()
                };
            }

            fileStream.Write(Encoding.ASCII.GetBytes(documentSyntax.ToString()));
            fileStream.Flush();
        }

        private void LoadDocumentSyntax(string configurationFilePath)
        {
            if (!File.Exists(configurationFilePath))
            {
                Log.Fatal("Configuration file does not exist, so cannot be loaded.");
                Environment.Exit(-1);
            }

            _DocumentSyntax = Toml.Parse(File.ReadAllText(configurationFilePath));
            _DocumentTable = _DocumentSyntax.ToModel();
            _LoggingTable = (TomlTable)_DocumentTable[_LOGGING_TABLE];
            _HostingTable = (TomlTable)_DocumentTable[_HOSTING_TABLE];
        }
    }
}
