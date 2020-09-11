#region

using System;
using System.Collections.Generic;

#endregion

namespace Disfigure.Diagnostics
{
    public static class Diagnostics
    {
        public enum DataType
        {
            Temporal
        }

        private static readonly Dictionary<Type, IDiagnosticGroup> _EnabledGroups;

        static Diagnostics() => _EnabledGroups = new Dictionary<Type, IDiagnosticGroup>();

        public static void EnableGroup<T>() where T : DiagnosticGroup, new()
        {
            if (_EnabledGroups.ContainsKey(typeof(T)))
            {
                throw new ArgumentException("Diagnostic group already enabled.", typeof(T).FullName);
            }
            else
            {
                T diagnosticGroup = new T();
                _EnabledGroups.Add(typeof(T), diagnosticGroup);
            }
        }

        public static void CommitData<T>(DataType dataType, object data) where T : IDiagnosticGroup
        {
            switch (dataType)
            {
                case DataType.Temporal when _EnabledGroups.ContainsKey(typeof(T)):
                    _EnabledGroups[typeof(T)].CommitData(dataType, data);
                    break;
                default:
                    throw new ArgumentException(
                        $"Diagnostic group '{typeof(T)}' has not been enabled. Please enable before attempting to commit data.");
            }
        }
    }
}
