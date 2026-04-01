using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure;

/// <summary>
/// Sample event bus for local testing for producer and consumer problem
/// </summary>
public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class;
    Task SubscribeAsync<T>(Func<T, CancellationToken, Task> handler, CancellationToken ct = default) where T : class;
}

public class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _channels = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class
    {
        var channel = GetOrCreateChannel<T>();
        await channel.Writer.WriteAsync(@event, ct);
        _logger.LogDebug("Published event {EventType}", typeof(T).Name);
    }

    public async Task SubscribeAsync<T>(Func<T, CancellationToken, Task> handler, CancellationToken ct = default) where T : class
    {
        var channel = GetOrCreateChannel<T>();
        _logger.LogInformation("Subscribed to {EventType}", typeof(T).Name);

        await foreach (var @event in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await handler(@event, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event {EventType}", typeof(T).Name);
            }
        }
    }

    private Channel<T> GetOrCreateChannel<T>() where T : class
    {
        return (Channel<T>)_channels.GetOrAdd(typeof(T), _ =>
            Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            }));
    }

    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            if (channel is IDisposable d) d.Dispose();
        }
    }
}
