#region

using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticsProviderNS;

#endregion

namespace Disfigure.Diagnostics
{
    public class ConstructionTime : TimeSpanDiagnosticData
    {
        public ConstructionTime(TimeSpan data) : base(data) { }
    }

    public class DecryptionTime : TimeSpanDiagnosticData
    {
        public DecryptionTime(TimeSpan data) : base(data) { }
    }

    public class PacketDiagnosticGroup : IDiagnosticGroup
    {
        private readonly List<ConstructionTime> _ConstructionTimes;
        private readonly List<DecryptionTime> _DecryptionTimes;

        public IReadOnlyList<ConstructionTime> ConstructionTimes => _ConstructionTimes;
        public IReadOnlyList<DecryptionTime> DecryptionTimes => _DecryptionTimes;

        public PacketDiagnosticGroup()
        {
            _ConstructionTimes = new List<ConstructionTime>();
            _DecryptionTimes = new List<DecryptionTime>();
        }

        public void CommitData<TDataType>(IDiagnosticData<TDataType> data)
        {
            switch (data)
            {
                case ConstructionTime constructionTime:
                    _ConstructionTimes.Add(constructionTime);
                    break;

                case DecryptionTime decryptionTime:
                    _DecryptionTimes.Add(decryptionTime);
                    break;
            }
        }

        public (double construction, double decryption) GetAveragePacketTimes()
        {
            double avgConstruction = _ConstructionTimes.DefaultIfEmpty()?.Average(time => ((TimeSpan)time).TotalMilliseconds) ?? 0d;
            double avgDecryption = _DecryptionTimes.DefaultIfEmpty()?.Average(time => ((TimeSpan)time).TotalMilliseconds) ?? 0d;

            return (avgConstruction, avgDecryption);
        }
    }
}
