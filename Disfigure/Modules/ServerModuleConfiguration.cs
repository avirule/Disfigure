#region

using System;
using System.IO;
using System.Net;
using System.Text;
using Serilog;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

#endregion

namespace Disfigure.Modules
{
    public class ServerModuleConfiguration
    {
        private const string _HOSTING_TABLE = "hosting";
        private const string _IP_ADDRESS_VALUE = "ipaddress";
        private const string _PORT_VALUE = "port";

        private DocumentSyntax? _DocumentSyntax;
        private TomlTable? _DocumentTable;
        private TomlTable? _HostingTable;

        public IPAddress HostingIPAddress => IPAddress.Parse((string)_HostingTable[_IP_ADDRESS_VALUE]);
        public ushort HostingPort => (ushort)_HostingTable[_PORT_VALUE];

        public ServerModuleConfiguration(bool executeInPlaceMode)
        {
            string configurationDirectoryPath = executeInPlaceMode
                ? @"./Configurations/"
                : $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/Disfigure/Configurations";

            string configurationFilePath = Path.Combine(configurationDirectoryPath, $"{GetType().Assembly.GetName()}.toml");

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
            File.Create(configurationFilePath);

            DocumentSyntax documentSyntax = new DocumentSyntax
            {
                Tables =
                {
                    new TableSyntax(_HOSTING_TABLE)
                    {
                        Items =
                        {
                            { _IP_ADDRESS_VALUE, IPAddress.Loopback.ToString() },
                            { _PORT_VALUE, (ushort)8898 }
                        }
                    }
                }
            };

            File.WriteAllText(configurationFilePath, documentSyntax.ToString(), Encoding.Unicode);
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
            _HostingTable = (TomlTable)_DocumentTable[_HOSTING_TABLE];
        }
    }
}
