# GraphQL API Gateway

A production-grade GraphQL gateway in **C# / .NET 8** that unifies three heterogeneous REST APIs — weather, news, and finance — behind a single typed schema with Redis-backed caching, rate limiting, and a React dashboard.

## Live Demo

The dashboard lets you query any city and stock ticker. A single GraphQL request fans out in parallel to Open-Meteo, NewsAPI, and Alpha Vantage, then returns everything in one response. The cache bar shows live hit/miss stats and response latency — query the same city twice to see the acceleration.

## Architecture

```
Client (React)
     │
     │  POST /graphql  (one request)
     ▼
┌─────────────────────────────────────────┐
│           Hot Chocolate Server           │
│  ┌──────────────┐  ┌─────────────────┐  │
│  │ Rate limiter │  │  Auth middleware │  │
│  └──────────────┘  └─────────────────┘  │
│                                          │
│  Query resolvers (parallel fan-out)      │
│  ┌────────────────────────────────────┐  │
│  │  Redis cache  (TTL per data type)  │  │
│  └────────────────────────────────────┘  │
└────────────┬──────────┬──────────┬───────┘
             │          │          │
             ▼          ▼          ▼
        Open-Meteo  NewsAPI  Alpha Vantage
        (weather)   (news)   (finance)
```

## Quick Start

### Option A — Docker Compose (recommended)

```bash
# Copy and configure environment
cp .env.example .env
# Add your API keys to .env (see below)

docker-compose up
```

- Frontend: http://localhost:3000
- GraphQL Playground: http://localhost:5000/graphql

### Option B — Local Development

**Requirements:** .NET 8 SDK, Node.js 20+, Redis

```bash
# Terminal 1: Start Redis
docker run -p 6379:6379 redis:7-alpine

# Terminal 2: Start the .NET gateway
cd src
dotnet run

# Terminal 3: Start the React frontend
cd frontend
npm install
npm run dev
```

## API Keys

Copy `.env.example` to `.env` and fill in your keys:

| Variable | Service | Free tier |
|---|---|---|
| `NEWS_API_KEY` | [newsapi.org](https://newsapi.org) | 100 req/day |
| `ALPHA_VANTAGE_KEY` | [alphavantage.co](https://www.alphavantage.co/support/#api-key) | 25 req/day |

Weather (Open-Meteo) is **completely free** — no key needed.

The app ships with graceful mock fallbacks if API keys are missing, so it works out of the box.

## Example Queries

Run these in the GraphQL Playground at `/graphql`:

### City dashboard (the flagship query)
```graphql
query {
  weather(city: "London") {
    city temperature condition icon
  }
  forecast(city: "London", days: 5) {
    date maxTemp minTemp condition
  }
  topHeadlines(query: "London", pageSize: 5) {
    title source publishedAt url
  }
  stockQuote(ticker: "MSFT") {
    ticker companyName price changePercent
  }
  cacheStats {
    hitCount missCount hitRate
  }
}
```

### Multiple stocks in parallel
```graphql
query {
  multipleQuotes(tickers: ["AAPL", "MSFT", "NVDA", "TSLA"]) {
    ticker companyName price change changePercent
  }
}
```

### News search
```graphql
query {
  searchNews(query: "artificial intelligence", pageSize: 10) {
    title description source publishedAt url
  }
}
```

## Caching Strategy

| Data type | TTL | Rationale |
|---|---|---|
| Weather | 5 minutes | Changes slowly, free API has rate limits |
| News | 10 minutes | Articles aren't real-time |
| Stock quotes | 30 seconds | Markets move fast but not per-request |

Cache keys are namespaced: `weather:london`, `stock:AAPL`, `headlines:london:all:6`

## Rate Limiting

Configured via `appsettings.json`:
- 60 requests/minute per IP
- 500 requests/hour per IP

Returns HTTP 429 with a `Retry-After` header on limit.

## Project Structure

```
graphql-gateway/
├── src/                    # .NET gateway
│   ├── Program.cs          # Service registration, middleware pipeline
│   ├── Query.cs            # GraphQL root resolvers
│   ├── Types.cs            # GraphQL schema types
│   ├── WeatherService.cs   # Open-Meteo REST client
│   ├── NewsService.cs      # NewsAPI REST client
│   ├── FinanceService.cs   # Alpha Vantage REST client
│   ├── CacheService.cs     # Redis abstraction with stats
│   └── Dockerfile
├── frontend/               # React + Vite dashboard
│   ├── src/
│   │   ├── App.tsx
│   │   ├── components/
│   │   │   ├── WeatherCard.tsx
│   │   │   ├── ForecastCard.tsx
│   │   │   ├── StockCard.tsx
│   │   │   ├── NewsCard.tsx
│   │   │   ├── CacheBar.tsx
│   │   │   └── QueryPanel.tsx
│   │   ├── hooks/
│   │   │   └── useDashboard.ts
│   │   └── lib/
│   │       └── graphql.ts  # Client + all query definitions
│   └── Dockerfile
└── docker-compose.yml
```

## Portfolio Talking Points

**System design:** The gateway pattern decouples consumers from upstream API volatility. Clients don't need to know about rate limits, auth, or data shape changes in any of the three upstream services — the gateway absorbs all of that.

**Performance:** Resolver-level caching with per-type TTLs means repeated queries for popular cities return in ~10ms instead of ~400ms. The cache bar in the UI makes this concrete and visible.

**Schema stitching:** Rather than exposing upstream REST response shapes directly, all three APIs are mapped to clean C# record types that form the GraphQL schema. The schema is owned by the gateway, not the upstreams.

**Resilience:** All three service clients have try/catch with graceful mock fallbacks. The Redis cache also degrades gracefully — if Redis is down, the app serves `NullCacheService` and continues working without caching.

**Observability:** Every request is logged via Serilog. Cache hit/miss counters are tracked in-memory and exposed via the `cacheStats` resolver, so the React UI can show live efficiency metrics.

## Tech Stack

- **C# .NET 8** — gateway server
- **Hot Chocolate 13** — GraphQL server (schema stitching, resolver execution)
- **StackExchange.Redis** — Redis client
- **AspNetCoreRateLimit** — per-IP rate limiting
- **Serilog** — structured logging
- **React 18 + Vite** — frontend dashboard
- **graphql-request** — lightweight GraphQL client
- **Recharts** — forecast bar chart
- **Docker + Docker Compose** — containerised deployment
