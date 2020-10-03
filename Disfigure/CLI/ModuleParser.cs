#region

using System;
using CommandLine;

#endregion

namespace Disfigure.CLI
{
    public static class ModuleParser
    {
        public static Parser Parser { get; }

        static ModuleParser() =>
            Parser = new Parser(settings =>
            {
                settings.HelpWriter = Console.Error;
                settings.CaseSensitive = false;
                settings.CaseInsensitiveEnumValues = true;
            });
    }
}
