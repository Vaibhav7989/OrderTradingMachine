using PositionPnLEngine.Services;
using Shared.Events;
using Shared.Infrastructure;

namespace PositionPnLEngine.Infrastructure;

/// <summary>
/// Hosted background service implementing the consumer side of the producer-consumer pattern.
/// Subscribes to TradeExecutedEvents from the event bus and automatically updates positions.
/// This is the async integration point between the two microservices.
/// </summary>
public class TradeEventConsumer : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly IPositionService _positionService;
    private readonly ILogger<TradeEventConsumer> _logger;

    public TradeEventConsumer(
        IEventBus eventBus,
        IPositionService positionService,
        ILogger<TradeEventConsumer> logger)
    {
        _eventBus = eventBus;
        _positionService = positionService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradeEventConsumer started — listening for trade events");

        await _eventBus.SubscribeAsync<TradeExecutedEvent>(async (@event, ct) =>
        {
            try
            {
                _logger.LogInformation(
                    "Consuming TradeExecutedEvent: {TradeId} {Symbol} {Qty} at {Price}",
                    @event.TradeId, @event.Symbol, @event.Quantity, @event.Price);

                // Update both sides of the trade
                _positionService.ApplyFill(@event.Symbol, "BUY", @event.Quantity, @event.Price);
                _positionService.ApplyFill(@event.Symbol, "SELL", @event.Quantity, @event.Price);
            }
            catch (Exception ex)
            {
                // CRITICAL: Log error but keep consumer alive!
                _logger.LogError(ex,
                    "Failed to process TradeExecutedEvent for {Symbol}. Event: {TradeId}",
                    @event.Symbol, @event.TradeId);
            }

            await Task.CompletedTask;
        }, stoppingToken);
    }
}
