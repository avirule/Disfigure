#region

using Disfigure.Collections;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;

#endregion

namespace Disfigure.Diagnostics
{
    public interface IDiagnosticData
    {
        public object Data { get; }
    }

    public interface IDiagnosticGroup
    {
        public void CommitData(IDiagnosticData data);
    }

    public static class DiagnosticsProvider
    {
        private static readonly Dictionary<Type, IDiagnosticGroup> _EnabledGroups;

        public static readonly ObjectPool<Stopwatch> Stopwatches = new ObjectPool<Stopwatch>(() => new Stopwatch());

        /// <summary>
        ///     Determines whether errors are emitted by the default logger when a particular <see cref="IDiagnosticGroup" /> has
        ///     not been enabled.
        /// </summary>
        public static bool EmitNotEnabledErrors { get; set; }

        static DiagnosticsProvider() => _EnabledGroups = new Dictionary<Type, IDiagnosticGroup>();

        /// <summary>
        ///     Enables given <see cref="TDiagnosticGroup" /> for logging data.
        /// </summary>
        /// <typeparam name="TDiagnosticGroup"><see cref="IDiagnosticGroup" /> to enable for data logging.</typeparam>
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

        /// <summary>
        ///     Commits <see cref="IDiagnosticData" /> to the given <see cref="IDiagnosticGroup" /> of type
        ///     <see cref="TDiagnosticGroup" />.
        /// </summary>
        /// <param name="diagnosticData"><see cref="IDiagnosticData" /> to commit.</param>
        /// <typeparam name="TDiagnosticGroup"><see cref="IDiagnosticGroup" /> to commit <see cref="IDiagnosticData" /> to.</typeparam>
        public static void CommitData<TDiagnosticGroup>(IDiagnosticData diagnosticData)
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