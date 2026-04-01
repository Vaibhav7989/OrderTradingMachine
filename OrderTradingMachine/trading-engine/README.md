# Trading Engine — .NET 8 Microservices

A high-performance, in-memory trading engine built as two independent microservices on .NET 8, implementing price-time priority order matching, async producer-consumer event flow using InMemory EventBus, and real-time Position & PnL tracking. Used swagger as well for manual use of the API's.

---

## Architecture Overview

'''
┌─────────────────────────────────┐        ┌─────────────────────────────────┐
│     Order Matching Engine        │        │     Position & PnL Engine        │
│         (port 5001)              │        │         (port 5002)              │
│                                 │        │                                 │
│  POST /api/orders               │        │    POST /api/positions/fill     │
│  DELETE /api/orders/{id}        │        │  POST /api/positions/price      │
│  GET /api/orders/{id}           │        │  GET  /api/positions/{symbol}   │
│                                 │        │  GET  /api/positions            │
│                                 │        │                                 │
│  ┌─────────────────────────┐   │        │  ┌─────────────────────────┐   │
│  │  OrderMatchingService   │   │        │  │    PositionService       │  │
│  │  (Singleton)            │   │        │  │    (Singleton)           │  │
│  │                         │   │        │  │                         │   │
│  │  ConcurrentDictionary   │   │        │  │  ConcurrentDictionary   │   │
│  │  <symbol, OrderBook>    │   │        │  │  <symbol, Position>     │   │
│  └──────────┬──────────────┘   │        │  └─────────────────────────┘   │
│             │publishes         │        │           ▲ consumes           │
│             ▼                  │        │           │                    │
│  ┌─────────────────────────┐   │        │  ┌────────┴────────────────┐   │
│  │   InMemoryEventBus      │◄──┼────────┼──│  TradeEventConsumer     │   │
│  │  (Channel<T> unbounded) │   │        │  │  (BackgroundService)    │   │
│  └─────────────────────────┘   │        │  └─────────────────────────┘   │
└─────────────────────────────────┘        └─────────────────────────────────┘
                    │ Shared project (Models, Events, EventBus)
'''

### Key Design

| Concern | Solution | Why |
|---|---|---|
| Order book data structure | 'SortedDictionary<price, Queue<Order>>' | O(log n) for the insertion; Queue enforces FIFO (time priority) |
| Thread safety (order book) | 'ReaderWriterLockSlim' | High read throughput for PRINT; exclusive write operations for NEW/CANCEL/MODIFY |
| Thread safety (position) | 'lock' per Position | Simpler; positions are low-contention per-symbol |
| Async inter service communications | 'System.Threading.Channels' | Lock-free, high-throughput producer-consumer;|
| Global order lookup | 'ConcurrentDictionary<orderId, Order>' | O(1) lookup for CANCEL/MODIFY without scanning books |
| Concurrency model | Async/await + BackgroundService | Non-blocking input and output operations|
'''

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)

---

## Running the Services

### dotnet run (two terminals)

'''bash
# Terminal 1 - Order Matching Engine (http://localhost:5001)
cd src/OrderMatchingEngine
dotnet run

# Terminal 2 - Position & PnL Engine (http://localhost:5002)
cd src/PositionPnLEngine
dotnet run
'''

### Swagger UI

- http://localhost:5001/swagger
- http://localhost:5002/swagger

---

## Running Tests

'''bash

# Run all tests with verbose output
dotnet test --verbosity normal
# Run only specific project
dotnet test tests/OrderMatchingEngine.Tests
dotnet test tests/PositionPnLEngine.Tests
'''

---

## API Reference

### Task 1 - Order Matching Engine (port 5001)

#### 'POST /api/orders' - NEW order

// Request
{
  "symbol": "AAPL",
  "side": "BUY",       
  "price": 150.00,
  "quantity": 100
}

// Response 200
{
  "orderId": "abc-123",
  "symbol": "AAPL",
  "side": "BUY",
  "price": 150.00,
  "quantity": 100,
  "remainingQuantity": 0,
  "status": "FILLED",
  "trades": [
    {
      "tradeId": "xyz-789",
      "price": 149.50,
      "quantity": 100,
      "executedAt": "2024-01-15T10:30:00Z"
    }
  ]
}


#### 'DELETE /api/orders/{orderId}' - CANCEL

'''json
// Response 200
{ "orderId": "abc-123", "status": "CANCELLED" }
'''

#### 'PATCH /api/orders/{orderId}' - MODIFY

'''json
// Request
{ "newPrice": 152.00, "newQuantity": 80 }

// Response 200
{ "orderId": "abc-123", "trades": [] }
'''

#### 'GET /api/orders/book/{symbol}' - PRINT order book

'''json
// Response 200
{
  "symbol": "AAPL",
  "timestamp": "2024-01-15T10:30:00Z",
  "bids": [
    { "price": 150.00, "orders": [{ "orderId": "...", "quantity": 100, "remainingQuantity": 100 }] }
  ],
  "asks": [
    { "price": 151.00, "orders": [{ "orderId": "...", "quantity": 50, "remainingQuantity": 50 }] }
  ]
}
'''

---

### Task 2 - Position & PnL Engine (port 5002)

#### 'POST /api/positions/fill' - FILL

'''json
// Request
{
  "symbol": "AAPL",
  "side": "BUY",
  "quantity": 100,
  "price": 150.00,
  "orderId": "random-123"   
}

// Response 200
{
  "symbol": "AAPL",
  "netQuantity": 100,
  "averagePrice": 150.00,
  "lastMarketPrice": 0,
  "realizedPnL": 0.00,
  "unrealizedPnL": 0.00,
  "totalPnL": 0.00,
  "timestamp": "2024-01-15T10:30:00Z"
}
'''

#### 'POST /api/positions/price' - PRICE update

'''json
// Request
{ "symbol": "AAPL", "price": 155.00 }

// Response 200
{
  "symbol": "AAPL",
  "netQuantity": 100,
  "averagePrice": 150.00,
  "lastMarketPrice": 155.00,
  "realizedPnL": 0.00,
  "unrealizedPnL": 500.00,
  "totalPnL": 500.00,
  "timestamp": "..."
}
'''

#### 'GET /api/positions/{symbol}' - PRINT single position

Returns 'PositionSnapshot' (same shape as above).

#### 'GET /api/positions' - PRINT all positions

'''json
{
  "positions": [],
  "summary": {
    "totalRealizedPnL": 1000.00,
    "totalUnrealizedPnL": 500.00,
    "totalPnL": 1500.00
  }
}
'''

---

## Matching Rules Used

| Rule | Implementation |
|---|---|
| BUY matches lowest SELL | SELL book is 'SortedDictionary' ascending - gives best ask |
| SELL matches highest BUY | BUY book is 'SortedDictionary' descending - gives best bid |
| Price-time priority | Queues at each price level - FIFO within same price |
| Partial fills | 'RemainingQuantity' tracked; order stays in book if partially filled |
| Resting price used | Trades execute at the resting order's price |

## PnL Calculation

| Formula | Description |
|---|---|
| 'avgPrice = (netQty x avgPrice + fillQty x fillPrice) / (netQty + fillQty)' | Running average cost for longs |
| 'realizedPnL += fillQty x (sellPrice - avgPrice)' | On each partial/full close of long |
| 'unrealizedPnL = netQty x (marketPrice - avgPrice)' | Marked to market |
| 'totalPnL = realizedPnL + unrealizedPnL' | Aggregate |

Short positions follow the inverse: 'realizedPnL += fillQty x (avgPrice - buyPrice)'.

---

## Concurrency / Producer-Consumer

Concurrency is achieved using the following points: 

1. **'InMemoryEventBus'** uses 'System.Threading.Channels.Channel<T>' : a lock-free, bounded/unbounded queue with async read/write. Multiple producers (order matching), multiple consumers (position engine) can be used.

2. **'TradeEventConsumer'** is a 'BackgroundService' that runs on its own task, continuously draining the channel via 'ReadAllAsync'.

3. **'OrderBook'** uses 'ReaderWriterLockSlim' for maximum concurrent read throughput for multiple PRINT operations with exclusive locks only during mutations.

4. **'Position'** uses a 'lock' per symbol instance appropriate for per-symbol single-digit write contention.


'''bash
curl http://localhost:5001/health
curl http://localhost:5002/health
'''
