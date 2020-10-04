#region

using System;

#endregion

namespace Disfigure.Net
{
    public readonly struct RetryParameters
    {
        public int Retries { get; }
        public TimeSpan Delay { get; }

        public RetryParameters(int retries, long delayMilliseconds) => (Retries, Delay) = (retries, TimeSpan.FromMilliseconds(delayMilliseconds));
    }
}
