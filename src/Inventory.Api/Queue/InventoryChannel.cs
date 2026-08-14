using System.Threading.Channels;

namespace Inventory.Api.Queue;

public sealed class InventoryChannel
{
    private readonly Channel<StockWorkItem> _channel = Channel.CreateUnbounded<StockWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<StockWorkItem> Writer => _channel.Writer;
    public ChannelReader<StockWorkItem> Reader => _channel.Reader;
}
