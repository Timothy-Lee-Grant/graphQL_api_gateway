# GraphQL API Gateway

A production-grade GraphQL gateway in **C# / .NET 8** that unifies three heterogeneous REST APIs — weather, news, and finance — behind a single typed schema. Features Redis-backed caching with per-type TTLs, per-IP rate limiting, structured logging, graceful mock fallbacks, and a React + Vite dashboard that visualizes live cache performance.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Project Structure](#project-structure)
4. [Tech Stack](#tech-stack)
5. [Quick Start](#quick-start)
6. [API Keys](#api-keys)
7. [Configuration](#configuration)
8. [GraphQL Schema & Operations](#graphql-schema--operations)
9. [Upstream Services](#upstream-services)
10. [Caching Strategy](#caching-strategy)
11. [Rate Limiting](#rate-limiting)
12. [Frontend Dashboard](#frontend-dashboard)
13. [Docker Deployment](#docker-deployment)
14. [Resilience & Fallbacks](#resilience--fallbacks)
15. [Observability](#observability)
16. [Troubleshooting](#troubleshooting)

---

## Overview

The gateway pattern solves a common problem: multiple upstream APIs each with their own auth, data shapes, rate limits, and reliability characteristics. Consumers shouldn't have to know about any of that. This gateway absorbs all of it and exposes a single, clean GraphQL endpoint.

A single request to `POST /graphql` can simultaneously fetch current weather, a 5-day forecast, top news headlines, and a live stock quote — all resolved in parallel, cached independently, and returned in one round trip.

The React dashboard makes the pattern tangible: query the same city twice and watch the response time drop from ~400ms to ~10ms as the cache kicks in.

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   React Frontend (port 3000)             │
│   WeatherCard · ForecastCard · StockCard · NewsCard      │
│   CacheBar · QueryPanel                                  │
└──────────────────────────┬──────────────────────────────┘
                           │  POST /graphql (one request)
                           ▼
┌─────────────────────────────────────────────────────────┐
│              .NET 8 GraphQL Gateway (port 5000)          │
│                                                          │
│  ┌──────────────────┐   ┌──────────────────────────┐    │
│  │  IP Rate Limiter │   │  Serilog Request Logging │    │
│  │  60 req/min      │   │  Structured console log  │    │
│  └──────────────────┘   └──────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │         Hot Chocolate GraphQL Server               │  │
│  │                                                    │  │
│  │   Query.GetWeather()        → WeatherService       │  │
│  │   Query.GetForecast()       → WeatherService       │  │
│  │   Query.GetTopHeadlines()   → NewsService          │  │
│  │   Query.SearchNews()        → NewsService          │  │
│  │   Query.GetStockQuote()     → FinanceService       │  │
│  │   Query.GetMultipleQuotes() → FinanceService       │  │
│  │   Query.GetCityDashboard()  → all three (parallel) │  │
│  │   Query.GetCacheStats()     → CacheService         │  │
│  └────────────────────┬───────────────────────────────┘  │
│                       │                                  │
│  ┌────────────────────▼───────────────────────────────┐  │
│  │            Redis Cache (TTL per data type)          │  │
│  │   weather:*    → 5 min  |  stock:*      → 30 sec   │  │
│  │   headlines:*  → 10 min | forecast:*   → 5 min     │  │
│  └────────────────────────────────────────────────────┘  │
└────────────┬──────────────┬──────────────┬───────────────┘
             │              │              │
             ▼              ▼              ▼
       Open-Meteo        NewsAPI     Alpha Vantage
    (weather + geo)    (headlines)   (stock quotes)
    free, no key        100/day         25/day
```

### Request flow

1. The React client fires a single `POST /graphql` with the `CityDashboard` query.
2. The rate limiter checks per-IP counters (in-memory, MemoryCache).
3. Hot Chocolate dispatches each resolver. Independent resolvers execute in parallel via `Task.WhenAll`.
4. Every resolver checks Redis first. On a cache hit it returns immediately; on a miss it calls the upstream REST API and writes the result back to Redis with the appropriate TTL.
5. The assembled response — weather, forecast, news, stock, cache stats — is returned in one payload.

---

## Project Structure

```
graphQL_api_gateway/
├── src/                          # .NET 8 gateway
│   ├── Program.cs                # DI registration, middleware pipeline, app bootstrap
│   ├── Query.cs                  # All GraphQL root resolvers
│   ├── Types.cs                  # C# record types that define the GraphQL schema
│   ├── WeatherService.cs         # Open-Meteo geocoding + weather REST client
│   ├── NewsService.cs            # NewsAPI headlines + search client
│   ├── FinanceService.cs         # Alpha Vantage stock quote client
│   ├── CacheService.cs           # Redis abstraction (+ NullCacheService fallback)
│   ├── GraphQLGateway.csproj     # NuGet package references
│   ├── appsettings.json          # Default configuration (rate limits, Redis, API keys)
│   └── Dockerfile                # Multi-stage .NET build
│
├── frontend/                     # React 18 + Vite dashboard
│   ├── src/
│   │   ├── App.tsx               # Root component: layout, city/ticker state, presets
│   │   ├── main.tsx              # React entry point
│   │   ├── index.css             # All application styles (CSS variables, dark theme)
│   │   ├── components/
│   │   │   ├── WeatherCard.tsx   # Current conditions: temp, feels-like, wind, humidity
│   │   │   ├── ForecastCard.tsx  # 5-day bar chart (Recharts)
│   │   │   ├── StockCard.tsx     # Price, change %, high/low/volume
│   │   │   ├── NewsCard.tsx      # Article list with relative timestamps
│   │   │   ├── CacheBar.tsx      # Live hit/miss/latency metrics bar
│   │   │   └── QueryPanel.tsx    # Collapsible: shows the raw GQL query sent
│   │   ├── hooks/
│   │   │   └── useDashboard.ts   # Fetch state machine (loading/data/error/latency)
│   │   └── lib/
│   │       └── graphql.ts        # graphql-request client + all query definitions + TS types
│   ├── vite.config.ts            # Dev proxy: /graphql → http://localhost:5000
│   ├── nginx.conf                # Prod: static files + reverse proxy to gateway
│   ├── package.json
│   └── Dockerfile                # Multi-stage Node build → nginx serve
│
├── docker-compose.yml            # gateway + redis + frontend (three-service stack)
├── .env.example                  # API key template
└── .gitignore
```

---

## Tech Stack

| Layer | Technology | Purpose |
|---|---|---|
| Gateway language | C# / .NET 8 | High-performance async server |
| GraphQL server | Hot Chocolate 13 | Schema definition, resolver execution, Banana Cake Pop playground |
| Caching | StackExchange.Redis + Redis 7 | Distributed cache with TTL |
| Rate limiting | AspNetCoreRateLimit 5 | Per-IP request throttling |
| Logging | Serilog 8 | Structured console logging with request middleware |
| Frontend framework | React 18 + Vite 5 | SPA dashboard |
| GraphQL client | graphql-request 7 | Lightweight typed GQL client |
| Charts | Recharts 2 | Forecast bar chart |
| Frontend container | nginx:alpine | Static file server + reverse proxy |
| Orchestration | Docker Compose | Multi-service local and production deployment |

---

## Quick Start

### Option A — Docker Compose (recommended)

Requires: Docker Desktop

```bash
# 1. Copy the environment template
cp .env.example .env

# 2. Optionally add your API keys to .env
#    The app works without them via deterministic mock data

# 3. Start all three services
docker-compose up
```

| Service | URL |
|---|---|
| React dashboard | http://localhost:3000 |
| GraphQL playground | http://localhost:5000/graphql |
| Redis | localhost:6379 |

### Option B — Local Development

Requires: .NET 8 SDK, Node.js 20+, Docker (for Redis)

```bash
# Terminal 1 — Redis
docker run -d -p 6379:6379 redis:7-alpine

# Terminal 2 — .NET gateway
cd src
dotnet run
# Listening at http://localhost:5000
# Playground at http://localhost:5000/graphql

# Terminal 3 — React frontend
cd frontend
npm install
npm run dev
# Running at http://localhost:3000
# /graphql requests are proxied to :5000 via Vite
```

---

## API Keys

Copy `.env.example` to `.env` and fill in your keys:

```env
NEWS_API_KEY=your_newsapi_key_here
ALPHA_VANTAGE_KEY=your_alphavantage_key_here
```

| Variable | Service | Free tier | Registration |
|---|---|---|---|
| `NEWS_API_KEY` | NewsAPI.org | 100 req/day | https://newsapi.org/register |
| `ALPHA_VANTAGE_KEY` | Alpha Vantage | 25 req/day | https://www.alphavantage.co/support/#api-key |

**Open-Meteo (weather) is completely free — no key required.**

Both paid services have graceful mock fallbacks so the app is fully functional without real API keys. Mock data is deterministically seeded (by ticker hash or query term) so it behaves consistently across restarts.

---

## Configuration

Configuration lives in `src/appsettings.json`. Docker Compose injects overrides as environment variables using ASP.NET Core's double-underscore convention.

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "NewsApi": {
    "ApiKey": "YOUR_NEWSAPI_KEY"
  },
  "AlphaVantage": {
    "ApiKey": "demo"
  },
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": false,
    "HttpStatusCode": 429,
    "GeneralRules": [
      { "Endpoint": "*", "Period": "1m",  "Limit": 60  },
      { "Endpoint": "*", "Period": "1h",  "Limit": 500 }
    ]
  }
}
```

Example environment variable overrides:

```
ConnectionStrings__Redis=redis:6379
NewsApi__ApiKey=abc123
AlphaVantage__ApiKey=xyz789
```

---

## GraphQL Schema & Operations

The interactive playground is at `http://localhost:5000/graphql`.

### Types

```graphql
type WeatherData {
  city:        String!
  temperature: Float!       # °C
  feelsLike:   Float!       # °C
  windSpeed:   Float!       # mph
  humidity:    Int!         # %
  condition:   String!      # e.g. "Partly cloudy"
  icon:        String!      # emoji e.g. "⛅"
  latitude:    Float!
  longitude:   Float!
}

type WeatherForecastDay {
  date:      String!   # ISO date e.g. "2025-06-01"
  maxTemp:   Float!    # °C
  minTemp:   Float!    # °C
  condition: String!
}

type NewsArticle {
  title:       String!
  description: String
  url:         String
  source:      String
  publishedAt: String   # ISO 8601
  urlToImage:  String
}

type StockQuote {
  ticker:        String!
  companyName:   String!
  price:         Float!
  change:        Float!    # absolute change
  changePercent: Float!    # percentage
  high:          Float!
  low:           Float!
  volume:        Long!
  lastUpdated:   String!
}

type CityDashboard {
  weather: WeatherData
  news:    [NewsArticle!]!
  stock:   StockQuote
}

type CacheStats {
  totalKeys: Long!
  hitCount:  Long!
  missCount: Long!
  hitRate:   Float!   # 0–100
}
```

### Queries

#### `weather(city: String!): WeatherData`
Current conditions for any city. City name is geocoded via Open-Meteo's free geocoding API to obtain lat/lon before fetching weather data.

```graphql
query {
  weather(city: "Tokyo") {
    city temperature feelsLike windSpeed humidity condition icon
  }
}
```

#### `forecast(city: String!, days: Int = 5): [WeatherForecastDay!]!`
Multi-day forecast. Defaults to 5 days.

```graphql
query {
  forecast(city: "Tokyo", days: 7) {
    date maxTemp minTemp condition
  }
}
```

#### `topHeadlines(query: String, category: String, pageSize: Int = 10): [NewsArticle!]!`
Top headlines from BBC News, Reuters, and Associated Press. Optionally filter by keyword or category (`technology`, `business`, `sports`, `health`, `science`, `entertainment`).

```graphql
query {
  topHeadlines(query: "London", pageSize: 6) {
    title source publishedAt url description
  }
}
```

#### `searchNews(query: String!, pageSize: Int = 10): [NewsArticle!]!`
Full-text search across all NewsAPI sources, sorted by `publishedAt` descending.

```graphql
query {
  searchNews(query: "artificial intelligence", pageSize: 10) {
    title description source publishedAt url
  }
}
```

#### `stockQuote(ticker: String!): StockQuote`
Real-time quote from Alpha Vantage. Falls back to deterministic mock data for known tickers (AAPL, MSFT, GOOGL, AMZN, META, NVDA, TSLA, NFLX) when the API key is `demo`.

```graphql
query {
  stockQuote(ticker: "NVDA") {
    ticker companyName price change changePercent high low volume
  }
}
```

#### `multipleQuotes(tickers: [String!]!): [StockQuote!]!`
Batch stock quotes. All tickers are fetched concurrently via `Task.WhenAll` — no sequential waiting.

```graphql
query {
  multipleQuotes(tickers: ["AAPL", "MSFT", "NVDA", "TSLA"]) {
    ticker companyName price change changePercent
  }
}
```

#### `cityDashboard(city: String!, stockTicker: String, newsCount: Int = 5): CityDashboard!`
Aggregated query: weather + top news + optional stock quote, all resolved in parallel server-side via `Task.WhenAll`. This is the flagship query demonstrated by the React dashboard.

```graphql
query {
  cityDashboard(city: "New York", stockTicker: "AAPL", newsCount: 5) {
    weather { city temperature condition icon }
    news    { title source publishedAt url }
    stock   { ticker price change changePercent }
  }
}
```

#### `cacheStats: CacheStats!`
Live Redis cache performance. `hitRate` is `hits / (hits + misses) * 100`. Hit/miss counters are tracked in-process with `Interlocked.Increment`.

```graphql
query {
  cacheStats {
    totalKeys hitCount missCount hitRate
  }
}
```

#### Full dashboard query (as sent by the React app)

```graphql
query CityDashboard($city: String!, $ticker: String, $newsCount: Int) {
  weather(city: $city) {
    city temperature feelsLike windSpeed humidity condition icon
  }
  forecast(city: $city, days: 5) {
    date maxTemp minTemp condition
  }
  topHeadlines(query: $city, pageSize: $newsCount) {
    title description url source publishedAt
  }
  stockQuote(ticker: $ticker) {
    ticker companyName price change changePercent high low volume lastUpdated
  }
  cacheStats {
    totalKeys hitCount missCount hitRate
  }
}
```

---

## Upstream Services

### Open-Meteo (Weather)
- **Geocoding:** `https://geocoding-api.open-meteo.com/v1/search`
- **Weather:** `https://api.open-meteo.com/v1/forecast`
- **Auth:** None required
- **Flow:** City name → geocode to (lat, lon) → fetch current conditions or daily forecast
- **WMO weather codes** are mapped to human-readable descriptions and emoji icons inside `WeatherService.cs`

### NewsAPI
- **Headlines:** `https://newsapi.org/v2/top-headlines`
- **Search:** `https://newsapi.org/v2/everything`
- **Auth:** `apiKey` query parameter
- **Default sources:** `bbc-news`, `reuters`, `associated-press`
- **Fallback:** 5 mock articles templated around the query term, with realistic relative timestamps

### Alpha Vantage (Finance)
- **Endpoint:** `https://www.alphavantage.co/query?function=GLOBAL_QUOTE`
- **Auth:** `apikey` query parameter
- **Fallback:** Deterministic mock quotes. Known tickers (AAPL, MSFT, GOOGL, AMZN, META, NVDA, TSLA, NFLX) have realistic base prices with a ±2% random variation seeded by ticker hash. Unknown tickers get a random-but-stable price in the $100–$600 range.

---

## Caching Strategy

All caching is behind the `ICacheService` interface in `CacheService.cs`. Cache keys are namespaced to prevent collisions between data types.

| Query | Cache key pattern | TTL | Rationale |
|---|---|---|---|
| `weather` | `weather:{city}` | 5 min | Conditions change slowly; API is free but good to conserve |
| `forecast` | `forecast:{city}:{days}` | 5 min | Daily forecast doesn't change minute-to-minute |
| `topHeadlines` | `headlines:{query}:{category}:{size}` | 10 min | Articles aren't real-time; API has low daily cap |
| `searchNews` | `news-search:{query}:{size}` | 10 min | Search results stable over minutes |
| `stockQuote` | `stock:{TICKER}` | 30 sec | Markets move fast, but per-request fetching wastes the 25/day free quota |

Cache writes are fire-and-forget: failures are logged as warnings and do not block the response. If Redis is unavailable at startup, the app substitutes `NullCacheService` (a no-op implementation) and continues without caching.

---

## Rate Limiting

Implemented with `AspNetCoreRateLimit`, backed by `MemoryCache` (no Redis dependency for this layer).

| Rule | Limit |
|---|---|
| Per IP, per minute | 60 requests |
| Per IP, per hour | 500 requests |

Exceeding a limit returns `HTTP 429 Too Many Requests`. The `RealIpHeader` is set to `X-Real-IP` for correct behavior behind a reverse proxy or load balancer.

To adjust limits, edit `IpRateLimiting.GeneralRules` in `appsettings.json`. To disable rate limiting entirely in development, set `"EnableEndpointRateLimiting": false`.

---

## Frontend Dashboard

The React app runs at `http://localhost:3000` and issues a single `CityDashboard` GraphQL query per user interaction.

### Components

| Component | Description |
|---|---|
| `WeatherCard` | Current temperature, feels-like, wind speed, humidity, condition label, emoji icon |
| `ForecastCard` | 5-day grouped bar chart (max/min temp per day) using Recharts with CSS variable theming |
| `StockCard` | Price, absolute change, percentage change, day high/low/volume with ▲/▼ colour coding |
| `NewsCard` | Scrollable article list with source name, relative timestamp (`2h ago`), description, and outbound links |
| `CacheBar` | Persistent top bar: response latency pill (green < 200ms / amber < 800ms / red), cache hit/miss pill, cumulative hits, misses, hit-rate percentage |
| `QueryPanel` | Collapsible bottom panel showing the exact GraphQL query sent, endpoint, response time, and whether data was served from cache |

### State management

`useDashboard.ts` is a single custom hook managing all query state: `loading`, `data`, `error`, `latencyMs`, and `cacheHit`. Cache hit detection works by comparing the `hitCount` from `cacheStats` between the previous and current requests.

### City presets

Four one-click presets are defined in `App.tsx`:

| Preset | City | Ticker |
|---|---|---|
| New York | New York | AAPL |
| London | London | MSFT |
| Tokyo | Tokyo | NVDA |
| Sydney | Sydney | TSLA |

### Vite dev proxy

In development, Vite proxies all `/graphql` requests to `http://localhost:5000`, so no CORS configuration is needed locally. In the Docker production stack, nginx handles this via `proxy_pass http://gateway:5000/graphql` in `nginx.conf`.

---

## Docker Deployment

`docker-compose.yml` defines three services on a shared `gateway-net` bridge network:

| Service | Build context | Port | Notes |
|---|---|---|---|
| `gateway` | `./src` | 5000 | Waits for Redis health check before starting |
| `redis` | `redis:7-alpine` | 6379 | AOF persistence, named volume `redis-data` |
| `frontend` | `./frontend` | 3000 | nginx proxies `/graphql` to `gateway:5000` |

Redis uses a named volume (`redis-data`) with append-only file persistence (`--appendonly yes`), so cached entries survive container restarts.

```bash
# Start all services
docker-compose up

# Rebuild after code changes
docker-compose up --build

# Backend only (no frontend)
docker-compose up gateway redis

# Tail logs for a specific service
docker-compose logs -f gateway
```

---

## Resilience & Fallbacks

| Failure scenario | Behaviour |
|---|---|
| Redis unavailable at startup | Logs a warning; substitutes `NullCacheService`; app continues without caching |
| Redis error during a request | GET/SET failures are caught and logged as warnings; request falls through to the upstream API |
| Open-Meteo geocoding fails | Returns `null` for `weather`/`forecast` fields; logs a warning |
| Open-Meteo weather fetch fails | Returns `null`; logs an error |
| NewsAPI unavailable or key missing | Returns 5 deterministic mock articles templated on the query term |
| Alpha Vantage unavailable or key is `demo` | Returns a mock quote seeded from the ticker's hash code |
| Any resolver throws | Caught at the service layer; returns `null` or empty list; error is logged with full stack trace |

The gateway is designed so that no single upstream failure causes an unhandled 500. Every service method has a top-level `try/catch` returning a safe fallback, and the GraphQL layer surface only shows errors in development (`IncludeExceptionDetails = IsDevelopment()`).

---

## Observability

**Structured request logging** via Serilog with the format:
```
[14:32:01 INF] HTTP POST /graphql responded 200 in 42.3ms
```

**Cache statistics** are exposed via the `cacheStats` GraphQL field and rendered live in the React `CacheBar`. Hit/miss counters use `Interlocked.Increment` for lock-free thread-safe counting.

**GraphQL instrumentation** is enabled via Hot Chocolate's `.AddInstrumentation()`, which attaches per-resolver timing data to the response extensions in development.

**Exception details** are included in GraphQL error responses only in the `Development` environment, preventing stack trace leakage in production.

---

## Troubleshooting

**Gateway can't connect to Redis**
- Verify Redis is running: `docker ps` or `redis-cli ping`
- Check that `ConnectionStrings__Redis` points to the correct host and port
- The gateway logs `"Redis unavailable — running without cache"` on startup and continues normally

**Frontend shows "Request failed: GraphQL request failed"**
- Confirm the .NET gateway is running on port 5000
- Check the browser console Network tab for the HTTP status code
- In Docker: `docker-compose logs gateway` to see startup errors

**News articles show repeated mock data**
- `NEWS_API_KEY` is missing or set to an invalid value
- The free NewsAPI tier (100 req/day) doesn't allow requests from localhost; the server-side gateway handles this correctly — ensure you're hitting the gateway, not calling NewsAPI directly

**Stock quotes don't reflect real market prices**
- `ALPHA_VANTAGE_KEY` is missing or set to `demo`, which triggers mock data
- Alpha Vantage free tier is 25 requests/day; the 30-second cache TTL is designed to conserve this budget
- Add a real key to `.env` and restart: `docker-compose up --build`

**HTTP 429 Too Many Requests**
- The per-IP rate limit (60 req/min) has been exceeded
- Reduce request frequency or raise the limit in `appsettings.json` under `IpRateLimiting.GeneralRules`
- Set `"EnableEndpointRateLimiting": false` to disable limits entirely in development
