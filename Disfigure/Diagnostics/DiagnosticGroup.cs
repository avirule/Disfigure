using System;
using System.Collections.Generic;

namespace Disfigure.Diagnostics
{
    public interface IDiagnosticGroup
    {
        public void CommitData(Diagnostics.DataType dataType, object data);
    }

    public abstract class DiagnosticGroup : IDiagnosticGroup
    {
        private readonly Dictionary<Diagnostics.DataType, List<object>> _Data;

        public DiagnosticGroup() => _Data = new Dictionary<Diagnostics.DataType, List<object>>();

        public void CommitData(Diagnostics.DataType dataType, object data)
        {
            switch (dataType)
            {
                case Diagnostics.DataType.Temporal when data is TimeSpan:
                    if (!_Data.ContainsKey(dataType))
                    {
                        _Data.Add(dataType, new List<object>());
                    }

                    _Data[dataType].Add(data);
                    break;
                default:
                    throw new ArgumentException("Provided data does not match type required for provided diagnostic type.", nameof(dataType));
            }
        }
    }
}
