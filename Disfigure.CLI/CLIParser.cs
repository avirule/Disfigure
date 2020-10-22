#region

using System;
using System.Collections.Generic;
using CommandLine;

#endregion


namespace Disfigure.CLI
{
    public static class CLIParser
    {
        public static T Parse<T>(IEnumerable<string> args) where T : class
        {
            T? parsed = null;

            Parser parser = new Parser(settings =>
            {
                settings.HelpWriter = Console.Error;
                settings.CaseSensitive = false;
                settings.CaseInsensitiveEnumValues = true;
            });

            parser.ParseArguments<T>(args).WithParsed(obj => parsed = obj);

            if (parsed is null) throw new ArgumentException($"Could not parse type {typeof(T)} from given arguments.");

            return parsed;
        }
    }
}
