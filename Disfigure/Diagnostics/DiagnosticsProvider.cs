#region

using System;
using System.Collections.Generic;
using Serilog;

#endregion

namespace Disfigure.Diagnostics
{
    public interface IDiagnosticData<out T>
    {
        public T Data { get; }
    }

    public interface IDiagnosticGroup
    {
        public void CommitData<T>(IDiagnosticData<T> data);
    }

    public static class DiagnosticsProvider
    {
        private static readonly Dictionary<Type, IDiagnosticGroup> _EnabledGroups;

        public static bool EmitNotEnabledErrors { get; set; }

        static DiagnosticsProvider() => _EnabledGroups = new Dictionary<Type, IDiagnosticGroup>();

        public static void EnableGroup<TDiagnosticGroup>() where TDiagnosticGroup : class, IDiagnosticGroup, new()
        {
            if (_EnabledGroups.ContainsKey(typeof(TDiagnosticGroup)))
            {
                Log.Error($"Diagnostic group '{typeof(TDiagnosticGroup).FullName}' is already enabled.");
            }
            else
            {
                TDiagnosticGroup diagnosticGroup = new TDiagnosticGroup();
                _EnabledGroups.Add(typeof(TDiagnosticGroup), diagnosticGroup);
            }
        }

        public static TDiagnosticGroup? GetGroup<TDiagnosticGroup>() where TDiagnosticGroup : class, IDiagnosticGroup =>
            _EnabledGroups[typeof(TDiagnosticGroup)] as TDiagnosticGroup ?? default;

        public static void CommitData<TDiagnosticGroup, TDataType>(IDiagnosticData<TDataType> diagnosticData)
            where TDiagnosticGroup : IDiagnosticGroup
        {
            if (_EnabledGroups.TryGetValue(typeof(TDiagnosticGroup), out IDiagnosticGroup? diagnosticGroup))
            {
                diagnosticGroup.CommitData(diagnosticData);
            }
            else if (EmitNotEnabledErrors)
            {
                Log.Error($"Diagnostic group '{typeof(TDiagnosticGroup).FullName}' has not been enabled. Please enable before commiting data.");
            }
        }
    }
}
