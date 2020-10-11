#region

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

#endregion

namespace Disfigure.Collections
{
    public class ChannelBag<T>
    {
        private readonly ChannelReader<T> _Reader;
        private readonly ChannelWriter<T> _Writer;

        public ChannelBag(bool singleReader, bool singleWriter)
        {
            Channel<T> channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = singleReader,
                SingleWriter = singleWriter
            });
            _Reader = channel.Reader;
            _Writer = channel.Writer;
        }

        public bool TryAdd(T item) => _Writer.TryWrite(item);
        public bool TryTake(out T item) => _Reader.TryRead(out item);

        public async ValueTask AddAsync(T item, CancellationToken cancellationToken = default)
        {
            await _Writer.WriteAsync(item, cancellationToken);
        }

        public async ValueTask<T> TakeAsync(bool waitForItems = true, CancellationToken cancellationToken = default)
        {
            if (waitForItems && await _Reader.WaitToReadAsync(cancellationToken)) { }

            return await _Reader.ReadAsync(cancellationToken);
        }
    }
}
