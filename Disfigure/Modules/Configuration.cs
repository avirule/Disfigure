#region

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using Serilog;
using Tomlyn.Syntax;

#endregion

namespace Disfigure.Modules
{
    public class Configuration
    {
        private AssemblyName _CallingAssembly;
        private bool _ExecuteInPlaceMode;

        private DocumentSyntax? _DocumentSyntax;

        public Configuration(bool executeInPlaceMode)
        {
            _CallingAssembly = GetType().Assembly.GetName();
            _ExecuteInPlaceMode = executeInPlaceMode;

            string configurationDirectoryPath = _ExecuteInPlaceMode
                ? @"./Configurations/"
                : $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/Disfigure/Configurations";

            string configurationFilePath = Path.Combine(configurationDirectoryPath, $"{_CallingAssembly.Name}.toml");

            ValidateFileStructure(configurationDirectoryPath, configurationFilePath);
            LoadDocumentSyntax(configurationFilePath);
        }

        private void ValidateFileStructure(string configurationDirectoryPath, string configurationFilePath)
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

        private void CreateConfigurationFile(string configurationFilePath)
        {
            File.Create(configurationFilePath);

            DocumentSyntax documentSyntax = new DocumentSyntax
            {
                Tables =
                {
                    new TableSyntax("hosting")
                    {
                        Items =
                        {
                            { "ipaddress", IPAddress.Loopback.ToString() },
                            { "port", 8898 }
                        }
                    }
                }
            };

            File.WriteAllText(configurationFilePath, documentSyntax.ToString(), Encoding.Unicode);
        }
    }
}
