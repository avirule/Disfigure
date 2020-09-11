#region

using System;
using System.Collections.Generic;

#endregion

namespace Disfigure.Diagnostics
{
    public readonly struct ConstructionTime : IDiagnosticData<TimeSpan>
    {
        public TimeSpan Data { get; }

        public ConstructionTime(TimeSpan data) => Data = data;

        public static explicit operator TimeSpan(ConstructionTime constructionTime) => constructionTime.Data;
    }

    public readonly struct DecryptionTime : IDiagnosticData<TimeSpan>
    {
        public TimeSpan Data { get; }

        public DecryptionTime(TimeSpan data) => Data = data;

        public static explicit operator TimeSpan(DecryptionTime decryptionTime) => decryptionTime.Data;
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

        public void CommitData<TDataType>(IDiagnosticData<TDataType> data)
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
    }
}
