#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

#endregion

namespace Disfigure.Diagnostics
{
    public class ConstructionTime : IDiagnosticData
    {
        private object _Data;
        public object Data => _Data;

        public ConstructionTime(TimeSpan data) => _Data = data;

        public static explicit operator TimeSpan(ConstructionTime constructionTime) => Unsafe.As<object, TimeSpan>(ref constructionTime._Data);
    }

    public class DecryptionTime : IDiagnosticData
    {
        private object _Data;
        public object Data => _Data;

        public DecryptionTime(TimeSpan data) => _Data = data;

        public static explicit operator TimeSpan(DecryptionTime decryptionTime) => Unsafe.As<object, TimeSpan>(ref decryptionTime._Data);
    }

    public class PacketDiagnosticGroup : IDiagnosticGroup
    {
        public List<ConstructionTime> ConstructionTimes { get; }
        public List<DecryptionTime> DecryptionTimes { get; }

        public PacketDiagnosticGroup()
        {
            ConstructionTimes = new List<ConstructionTime>();
            DecryptionTimes = new List<DecryptionTime>();
        }

        public void CommitData(IDiagnosticData data)
        {
            switch (data)
            {
                case ConstructionTime constructionTime:
                    ConstructionTimes.Add(constructionTime);
                    break;
                case DecryptionTime decryptionTime:
                    DecryptionTimes.Add(decryptionTime);
                    break;
            }
        }

        public (double construction, double decryption) GetAveragePacketTimes()
        {
            double avgConstruction = ConstructionTimes.DefaultIfEmpty()?.Average(time => ((TimeSpan)time).TotalMilliseconds) ?? 0d;
            double avgDecryption = DecryptionTimes.DefaultIfEmpty()?.Average(time => ((TimeSpan)time).TotalMilliseconds) ?? 0d;

            return (avgConstruction, avgDecryption);
        }
    }
}
