#region

using System;

#endregion

namespace Disfigure.Net
{
    public class PendingPing
    {
        public Guid Identity { get; }
        public TimeSpan PingLifetime { get; }

        public PendingPing()
        {
            Identity = Guid.NewGuid();
            PingLifetime = TimeSpan.Zero;
        }
    }
}
