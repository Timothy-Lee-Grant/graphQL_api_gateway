# Engineering Concepts: The GraphQL API Gateway

> This document uses the GraphQL API Gateway project as a lens to teach real software engineering and systems thinking. The goal isn't to document the project — it's to explain *why* the project is built the way it is, what problems it solves that you'll encounter throughout your career, and how the ideas inside it scale from a side project to infrastructure that powers millions of requests.

---

## Table of Contents

1. [The Problem This Solves](#1-the-problem-this-solves)
2. [The API Gateway Pattern](#2-the-api-gateway-pattern)
3. [Why GraphQL Instead of REST](#3-why-graphql-instead-of-rest)
4. [The Cache-Aside Pattern](#4-the-cache-aside-pattern)
5. [Parallel Execution and Why It Matters](#5-parallel-execution-and-why-it-matters)
6. [Graceful Degradation and Fallbacks](#6-graceful-degradation-and-fallbacks)
7. [Rate Limiting](#7-rate-limiting)
8. [The Abstraction Layer](#8-the-abstraction-layer)
9. [Observability](#9-observability)
10. [What This Could Become](#10-what-this-could-become)
11. [The Mental Models to Carry Forward](#11-the-mental-models-to-carry-forward)

---

## 1. The Problem This Solves

### Start with the pain

Imagine you're building a dashboard for a city. You need weather data, current news, and a stock price. You have three APIs available: Open-Meteo, NewsAPI, and Alpha Vantage.

The naive approach is to call all three directly from the frontend:

```
Browser
  ├── fetch("https://api.open-meteo.com/...")
  ├── fetch("https://newsapi.org/v2/...")
  └── fetch("https://www.alphavantage.co/...")
```

This works. For about ten minutes. Then the problems start stacking up.

**Problem 1: Your API keys are exposed.**
Any request made from a browser is visible to anyone with DevTools open. Your NewsAPI key — the one you're paying for — is now public. Someone can scrape it, use it, and burn your quota in minutes.

**Problem 2: CORS.**
APIs you don't control have their own CORS policies. Open-Meteo is permissive. NewsAPI blocks client-side requests on their free tier. Alpha Vantage has inconsistent behavior. You're now at the mercy of every upstream service's security policy.

**Problem 3: You make three round trips for every page load.**
Each request has latency. If weather takes 200ms, news takes 350ms, and stocks take 180ms, you're waiting for all three before the page is useful. And that's assuming none of them fail.

**Problem 4: You're calling these APIs hundreds of times a day for the same data.**
Weather in London doesn't change every second. But if 500 users load your dashboard, you make 500 requests to Open-Meteo. Most of those responses are identical. You're burning quota and adding latency for no reason.

**Problem 5: If any API changes its response format, you update every client.**
Alpha Vantage once changed a field name in their response. Every piece of code that read that field broke simultaneously. If you have a mobile app, a web app, and a desktop app all calling the API directly, that's three codebases to patch.

These aren't theoretical problems. Every team that skips the gateway eventually builds one after they've been burned by all five.

### The insight

The root cause of all five problems is the same: **your client is coupled directly to your upstream services.** The client knows too much — it knows the API URLs, the auth mechanisms, the response shapes, the rate limits. Every piece of knowledge the client holds about an upstream service is a liability.

A gateway removes that coupling. The client knows exactly one thing: `POST /graphql`. Everything else is the gateway's problem.

---

## 2. The API Gateway Pattern

### What it is

An API gateway is a server that sits between your clients and your backend services. Its job is to receive requests, do work (authentication, caching, transformation, routing), and return responses. To the client, it looks like a single service. Behind it, it may orchestrate dozens.

```
Client  →  Gateway  →  Service A
                   →  Service B
                   →  Service C
```

This pattern is one of the most important in distributed systems. You'll encounter it everywhere — AWS API Gateway, Kong, Netflix Zuul, and this project are all implementations of the same idea.

### The gateway's jobs

A gateway isn't just a proxy. In this project alone, the gateway is doing six distinct things:

**1. Aggregation** — combining data from three APIs into one response.

**2. Caching** — storing responses in Redis so repeated requests don't hit upstream APIs.

**3. Rate limiting** — protecting upstream APIs (and your quota) from too many requests.

**4. Transformation** — mapping the upstream APIs' response shapes to your own clean schema. The client never sees Alpha Vantage's raw JSON.

**5. Authentication (potential)** — the gateway is the right place to verify tokens, API keys, or sessions before any upstream service ever sees a request.

**6. Resilience** — if an upstream fails, the gateway handles it gracefully rather than propagating the failure to the client.

### Why a single entry point is powerful

When you centralize all these concerns in the gateway, a remarkable thing happens: **every client gets them for free.** 

If you add authentication to the gateway, your mobile app, web app, and any future clients are all authenticated without any of them needing to change. If you add a cache, all clients benefit immediately. If an upstream API changes its response format, you update the gateway's transformation layer once, and nothing else breaks.

This is the power of indirection. Every layer of software architecture is really just controlled indirection — hiding complexity behind a stable interface.

### The tradeoffs

No pattern is free. A gateway introduces:

- **A new failure point.** If the gateway goes down, nothing works. This means the gateway needs to be the most reliable piece of your infrastructure, which means load balancing, health checks, and redundancy.
- **A new bottleneck.** All traffic flows through one place. The gateway must be fast and scalable.
- **Operational complexity.** You now have more infrastructure to deploy, monitor, and maintain.

For a side project, these tradeoffs are worth it even for the learning alone. For a production system, these tradeoffs are almost always worth it because the alternative — tightly coupled clients — becomes unmanageable at scale.

---

## 3. Why GraphQL Instead of REST

### REST's fundamental tension

REST is a set of architectural conventions for building APIs using HTTP. It's been the dominant style for over a decade, and it's excellent. But it has a structural problem that shows up in every sufficiently complex system: **the client never quite gets exactly what it needs.**

Consider this: you want to display a city dashboard. You need weather, news, and a stock price. In a REST world, those are three separate endpoints:

```
GET /weather?city=London
GET /news?query=London
GET /stocks?ticker=MSFT
```

Three requests. Three round trips. Three opportunities for failure. And each endpoint returns a fixed shape — you get everything the server decided you need, whether you wanted all of it or not.

This is the **overfetching** problem. You asked for a weather result and got 40 fields. You needed 6.

The inverse is also common: **underfetching**. You make a request and don't get quite enough. You need to make a second request to get the related data. This cascade of requests is called the N+1 problem and it's one of the most common performance killers in REST APIs.

### What GraphQL changes

GraphQL flips the model. Instead of the server deciding the response shape, the client declares exactly what it wants:

```graphql
query {
  weather(city: "London") {
    temperature condition icon
  }
  topHeadlines(query: "London", pageSize: 5) {
    title source
  }
  stockQuote(ticker: "MSFT") {
    price changePercent
  }
}
```

One request. The server returns exactly those fields — nothing more. The client is in control of the shape.

But the deeper value of GraphQL isn't the query language. It's **the schema**.

### The schema as a contract

A GraphQL schema is a typed, machine-readable description of everything your API can do. Every type, every field, every argument, with types:

```graphql
type WeatherData {
  city:        String!
  temperature: Float!
  condition:   String!
}
```

The `!` means non-null — the server guarantees this field will always be present. This is a *contract*. The client and server agree, in writing, on what data looks like.

This has enormous practical benefits:

- **Auto-generated documentation.** The schema *is* the docs. The playground at `/graphql` introspects the schema and generates interactive docs automatically.
- **Type safety.** In TypeScript, you can generate types directly from the schema. The types in `graphql.ts` — `WeatherData`, `StockQuote`, etc. — could be auto-generated from the schema. If the schema changes, the TypeScript types change, and the compiler catches everywhere the code is broken.
- **Discoverability.** New engineers on the team can explore the entire API surface from the playground without reading any documentation.

### When to use GraphQL vs REST

GraphQL isn't always the right choice. REST is simpler, more cacheable at the HTTP layer (GET requests can be cached by CDNs), and better understood by most tooling.

Use GraphQL when:
- Multiple clients (mobile, web, desktop) need different subsets of the same data
- You're aggregating data from multiple sources
- The data graph is complex (many related types)
- Developer experience and discoverability are priorities

Use REST when:
- You're building a simple CRUD API
- HTTP-level caching (CDN, browser cache) matters
- The team is more familiar with REST conventions
- You're building a public API for external developers who expect REST

The honest answer is: most production systems use both. Public APIs tend to be REST. Internal APIs between services tend to be gRPC. Developer-facing aggregation layers tend to be GraphQL.

---

## 4. The Cache-Aside Pattern

### Why caching exists

A cache is a temporary storage layer that holds copies of data so future requests can be served faster. The fundamental insight is that **reading is cheap, computing is expensive.**

If fetching weather for London takes 300ms and involves two HTTP calls (geocode + weather), but reading from Redis takes 1ms, then after the first request, every subsequent request for London's weather is 300x faster. The data is the same; the cost to retrieve it drops by three orders of magnitude.

### The cache-aside pattern

This project uses the most common caching strategy: **cache-aside** (also called lazy loading). The pattern is:

```
1. Check the cache for the key
2. If found (cache HIT):  return the cached value
3. If not found (cache MISS):
   a. Fetch the real data from the source
   b. Write it to the cache with a TTL
   c. Return the data
```

In code, every resolver in `Query.cs` follows this exact pattern:

```csharp
var key = $"weather:{city.ToLower().Trim()}";
var cached = await cache.GetAsync<WeatherData>(key);
if (cached is not null) return cached;           // cache hit

var result = await weather.GetCurrentAsync(city); // cache miss: fetch
if (result is not null) 
    await cache.SetAsync(key, result, WeatherTtl); // write to cache
return result;
```

This pattern is explicit and simple: the application code manages the cache. It's not magic.

### TTL: Time To Live

Every cached value has a TTL — the duration before it expires and must be re-fetched. Choosing the right TTL is a genuine engineering judgment call, not a mechanical decision.

Ask: *how stale can this data be before it harms the user?*

- **Weather (5 min TTL):** Weather changes, but not second-to-second. A user checking weather for London at 2:00pm and 2:03pm should see the same result. 5 minutes is accurate enough, and it dramatically reduces calls to Open-Meteo.
- **News (10 min TTL):** Breaking news feels urgent, but a 10-minute window is imperceptible for most users. The bigger driver here is the 100 req/day quota on the free tier.
- **Stocks (30 sec TTL):** Stock prices move constantly during market hours. 30 seconds is a compromise between freshness and quota conservation (Alpha Vantage's free tier is 25 req/day). A financial application with a paid API tier might use a 1-second TTL or even skip caching entirely.

There's no formula for the right TTL. It depends on the data's rate of change, the user's expectations, and the cost of fetching fresh data.

### The cache invalidation problem

There's a famous saying in computer science: *"There are only two hard things: cache invalidation and naming things."* (Phil Karlton)

Cache-aside with TTL sidesteps the hardest part of cache invalidation by letting entries expire naturally. But this creates a class of problems: what if the data changes before the TTL expires?

Example: a user posts a news article at 2:00pm. The cache has a stale headline list that won't expire until 2:10pm. For 10 minutes, that article won't appear.

For most applications, this is acceptable — the cost of slightly stale data is lower than the cost of complex invalidation logic. But for some domains (financial data, inventory levels, real-time scores), stale data is a product-critical bug. Those systems use different strategies:

- **Write-through caching:** Update the cache at the same time you update the source of truth.
- **Event-driven invalidation:** When data changes, publish an event that deletes the cache key.
- **Cache short enough that staleness doesn't matter:** A 1-second TTL effectively makes cache invalidation irrelevant.

Recognizing *which strategy a situation calls for* is a skill you develop by seeing the consequences of getting it wrong.

### Cache key design

Cache keys are just strings, but their design matters enormously.

In this project, keys look like:
- `weather:london`
- `stock:AAPL`
- `headlines:london:all:6`
- `forecast:tokyo:5`

The key includes every variable that changes the result. If the result for London weather at pageSize 6 is different from London weather at pageSize 10, those must be different cache entries — hence `headlines:london:all:6` vs `headlines:london:all:10`.

A common bug is building cache keys that aren't specific enough. If two different queries accidentally share a cache key, they'll return each other's data. This is called a **cache collision** and it produces subtle, hard-to-reproduce bugs.

### Redis specifically

Redis is an in-memory data store. "In-memory" means the data lives in RAM, not on disk, which is why reads are so fast (microseconds vs milliseconds for disk I/O).

Redis is a separate process from your application server. This is important: when you deploy multiple instances of the gateway (for load balancing), they all share the same Redis instance. The cache is a shared resource across the entire fleet, not local to one server.

If you cached in the application's memory (a `Dictionary<string, object>`), each server instance would have its own private cache. The first request to each server would be a cache miss. Shared Redis means any server's cache hit benefits every server.

---

## 5. Parallel Execution and Why It Matters

### Sequential vs parallel

When you need three pieces of data, you have two options for fetching them:

**Sequential (one after another):**
```
start → fetch weather (300ms) → fetch news (250ms) → fetch stocks (200ms) → done
total: 750ms
```

**Parallel (all at once):**
```
start → fetch weather (300ms)  ─┐
      → fetch news (250ms)     ─┤→ done
      → fetch stocks (200ms)   ─┘
total: 300ms (the slowest one)
```

The parallel approach is faster by the sum of all but the slowest operation. In this example, 450ms saved.

This is exactly what `Task.WhenAll` does in C#:

```csharp
var weatherTask = GetWeather(city, weather, cache);
var newsTask    = GetTopHeadlines(city, null, newsCount, news, cache);
var stockTask   = stockTicker is not null 
    ? GetStockQuote(stockTicker, finance, cache) 
    : Task.FromResult<StockQuote?>(null);

await Task.WhenAll(weatherTask, newsTask, stockTask);
```

All three start simultaneously. The `await Task.WhenAll` line doesn't return until all three have finished — but they're all running concurrently.

### When you can parallelize

You can only parallelize operations that are **independent** of each other. If operation B needs the result of operation A, B must wait for A. This dependency chain is called a **data dependency**, and it forces sequential execution.

In the city dashboard:
- Weather and news are independent ✓
- Weather and stocks are independent ✓  
- News and stocks are independent ✓

All three can run in parallel. If the design required "fetch the city's coordinates first, then use those coordinates to find local news," then news would have a data dependency on weather and couldn't be parallelized with it.

Recognizing which operations have data dependencies — and structuring code to eliminate unnecessary ones — is one of the highest-leverage performance skills in backend engineering.

### Async/await and what it actually means

`async/await` is a syntax for writing asynchronous code that looks sequential. It's not parallelism by itself.

When you `await` something, the current thread is *released* to do other work while waiting for the I/O operation (network call, database query, file read) to complete. When the I/O finishes, execution resumes.

This is distinct from threading. You don't need 100 threads to handle 100 concurrent requests. You need threads to be free when they're waiting on I/O. `async/await` achieves this by making the waiting non-blocking.

The mental model: imagine a waiter at a restaurant. A synchronous waiter takes your order, walks to the kitchen, stands there until the food is ready, then brings it back. An async waiter takes your order, walks to the kitchen, *puts in the order*, then goes to take another table's order. When the kitchen calls the order up, the waiter (or any available waiter) delivers it.

The kitchen (I/O) does the actual waiting. The waiter (thread) stays productive.

This is why .NET can handle thousands of concurrent GraphQL requests on a handful of threads. Most of the time, threads aren't doing computation — they're waiting on network responses. Async I/O keeps those threads available for other work during the wait.

---

## 6. Graceful Degradation and Fallbacks

### What "resilient" actually means

A resilient system doesn't mean a system that never fails. It means a system that **fails in controlled, predictable ways** that minimize harm to the user.

There's a spectrum of failure responses:

| Response | Example | Impact |
|---|---|---|
| Crash | Unhandled exception kills the process | Total outage |
| Error propagation | Upstream error becomes a 500 to client | Poor user experience |
| Silent failure | Return null, log nothing | Mysterious bugs |
| Graceful degradation | Return mock/stale data, log the error | Partial functionality |
| Full resilience | Retry, circuit break, recover | No visible impact |

This project implements graceful degradation. If Alpha Vantage is down, the stock card shows mock data instead of crashing or showing an error. The user gets a working page, and the error is logged for the engineer to investigate.

### The fallback hierarchy

When designing fallbacks, think in tiers:

```
1. Cache (Redis) — fastest, no upstream cost
2. Live upstream API — fresh but slow and quota-limited
3. Stale cache — expired but better than nothing
4. Mock/default data — fabricated but shows the UI works
5. Error state — explicit failure, honest with the user
```

This project uses tiers 1, 2, and 4. Tier 3 (serving stale cache on upstream failure) is a common production enhancement — if Redis has an expired entry and the upstream is down, serving the expired data is better than serving nothing.

### Try/catch as a design decision

Every service method in this project wraps its logic in `try/catch`:

```csharp
public async Task<WeatherData?> GetCurrentAsync(string city, CancellationToken ct = default)
{
    try
    {
        // ... all the real logic
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to fetch weather for {City}", city);
        return null;
    }
}
```

This is a deliberate design choice. The method signature returns `WeatherData?` (nullable) — it's honest that it might return nothing. The catch doesn't swallow the error silently; it logs it. And it doesn't crash the request.

Notice what happens in the GraphQL schema when this returns null: the `weather` field is nullable. The client receives a response with `"weather": null` instead of an error. The news and stocks still come back. The page still renders — just without weather data.

This is graceful degradation: **partial success is better than total failure**.

### Circuit breakers

This project doesn't implement circuit breakers, but they're the natural next step in resilience engineering, and worth understanding.

The problem: if an upstream service is down, and you don't know it yet, every request will wait for the timeout (say, 30 seconds) before failing. Under load, you'll have thousands of threads all waiting for a service that isn't coming back. Your entire system slows to a crawl waiting for a dead service.

A circuit breaker is like an electrical circuit breaker in your house: when it detects too many failures, it "trips" and stops sending requests to the failed service entirely. For the next 30 seconds (the "open" state), all calls fail immediately instead of waiting. After 30 seconds, it "half-opens" and lets one test request through. If that succeeds, the circuit closes and normal traffic resumes.

The benefit: **fail fast**. Don't waste time and resources waiting for something that isn't working. Libraries like Polly (C#), Resilience4j (Java), and Hystrix (Java) implement this pattern.

---

## 7. Rate Limiting

### The two reasons to rate limit

Rate limiting means restricting how many requests a client can make in a given time window. There are two different motivations for it, and they're often conflated:

**1. Protecting your own system.** Too many requests can overwhelm your server, exhaust your database connections, or cause memory pressure. Rate limiting keeps your system stable under load.

**2. Protecting upstream systems (and your quota).** Your gateway calls external APIs that have their own rate limits. If your gateway receives 1,000 requests per minute, and NewsAPI allows 100 requests per day, you'll exhaust your quota in seconds without some form of throttling.

This project's rate limiter primarily serves the second purpose. 60 requests/minute per IP is loose enough that normal users will never hit it, but tight enough to prevent abuse from a single client hammering the gateway.

### How the rate limiter works

The `AspNetCoreRateLimit` library uses a **fixed window** algorithm:

```
Window: 1 minute
Limit:  60 requests
```

Every IP address gets a counter. Each request increments the counter. When the counter hits 60, the next request gets a 429 response instead of being processed. The counter resets at the start of each minute window.

This is simple and cheap to implement but has a known problem: **the thundering herd at window boundaries**. A client could make 60 requests at 11:59:59, the window resets, and they immediately make 60 more at 12:00:00. That's 120 requests in 2 seconds — not what "60 per minute" intuitively means.

More sophisticated algorithms include:

- **Sliding window log:** Track the timestamp of every request; reject requests if more than N have occurred in the past minute. Accurate but memory-intensive.
- **Token bucket:** A bucket fills with tokens at a steady rate (e.g., 1 token/second). Each request consumes a token. Empty bucket = rejected request. This allows bursting up to the bucket size, then enforces a steady rate.
- **Leaky bucket:** Requests enter a queue and are processed at a fixed rate. Excess requests are rejected or queued.

For most applications, the fixed window is good enough. The token bucket is preferred when you want to allow short bursts (e.g., a user opening an app and loading multiple things at once) without allowing sustained high throughput.

### Distributed rate limiting

The rate limiter in this project stores counters in `MemoryCache` — the application's own memory. This works when you have one server. When you have multiple gateway instances behind a load balancer, each instance has its own counter. A client that cycles between instances can make N * (limit) requests, effectively bypassing the limit.

Distributed rate limiting stores counters in Redis, so all instances share state. The `INCR` and `EXPIRE` commands in Redis make this atomic and safe:

```
INCR ip:192.168.1.1:2025-06-01T14:32
EXPIRE ip:192.168.1.1:2025-06-01T14:32 60
```

This is one of the most common real-world uses of Redis beyond caching.

---

## 8. The Abstraction Layer

### Mapping upstream shapes to your own types

One of the most important things the gateway does is **own its schema**. When Alpha Vantage returns this:

```json
{
  "Global Quote": {
    "01. symbol": "AAPL",
    "05. price": "189.25",
    "09. change": "+1.42",
    "10. change percent": "0.7567%"
  }
}
```

The gateway maps it to:

```csharp
public record StockQuote(
    string Ticker,
    string CompanyName,
    double Price,
    double Change,
    double ChangePercent,
    ...
);
```

The client never sees the `"05. price"` key name or the `"0.7567%"` string that needs to have its `%` stripped before parsing. The gateway absorbs that messiness.

This creates a **seam** — a boundary where one thing ends and another begins. The seam is valuable because:

**It localizes change.** If Alpha Vantage renames `"05. price"` to `"price"` in a new API version, you update one mapping in `FinanceService.cs`. Nothing else changes. The schema the client sees is unaffected.

**It normalizes inconsistency.** Different APIs have wildly different conventions. Some return dates as Unix timestamps, some as ISO 8601 strings, some as "June 1st, 2025." The gateway can normalize all of these to one format before they ever leave the building.

**It hides secrets.** The API key structure of the upstream URL, the authentication headers, the quirks of the response format — none of that leaks to the client.

### Interfaces and dependency inversion

Notice that in this project, the services are accessed through interfaces:

```csharp
public interface IWeatherService
{
    Task<WeatherData?> GetCurrentAsync(string city, CancellationToken ct = default);
    Task<IEnumerable<WeatherForecastDay>> GetForecastAsync(string city, int days = 5, CancellationToken ct = default);
}
```

The `Query.cs` resolvers take `[Service] IWeatherService weather` — the interface, not the concrete `WeatherService` class.

This is the **Dependency Inversion Principle** (the D in SOLID): high-level modules (the resolvers) should not depend on low-level modules (the HTTP client implementation). Both should depend on abstractions (the interface).

In practice this means:

1. **Testability.** You can write unit tests for the resolvers by injecting a fake `IWeatherService` that returns predetermined data. No HTTP calls needed in tests.
2. **Swappability.** If you want to switch from Open-Meteo to a paid weather provider, you write a new class implementing `IWeatherService` and change one line in `Program.cs`. The resolvers don't care.
3. **The same pattern is why `ICacheService` exists** — the `NullCacheService` (used when Redis is unavailable) and `RedisCacheService` both implement `ICacheService`. The resolvers don't need to know which one they got.

---

## 9. Observability

### The three pillars

"Observability" is the degree to which you can understand what your system is doing from the outside. In distributed systems, you can't just attach a debugger — you need to understand what happened hours ago, across multiple servers, in production. Observability is how you do that.

The three pillars are:

**Logs** — timestamped records of discrete events. "At 14:32:01, a request for London weather succeeded in 43ms." Good for investigating specific incidents.

**Metrics** — numerical measurements over time. "The average response time over the last 5 minutes is 67ms. The error rate is 0.2%." Good for dashboards, alerting, and spotting trends.

**Traces** — the path a single request took through your system, with timing for each step. "This request spent 2ms in the rate limiter, 1ms checking the cache (cache miss), 340ms calling Open-Meteo, 3ms writing to Redis." Good for identifying bottlenecks.

This project implements logs (Serilog) and a basic form of metrics (cache hit/miss counters via `cacheStats`). A production system would add all three.

### Structured logging

Look at the Serilog configuration in `Program.cs`:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: 
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

And in the services:

```csharp
logger.LogWarning(ex, "Geocoding failed for {City}", city);
logger.LogError(ex, "Failed to fetch weather for {City}", city);
```

Notice the `{City}` placeholder. This isn't just string interpolation. With structured logging, `{City}` becomes a *queryable field* in the log output. In a log aggregation system like Elasticsearch or Splunk, you can search for `City = "London"` and find every log entry related to London — across all instances, all time.

Compare:
- Unstructured: `"Geocoding failed for London"` — searchable only by substring
- Structured: `{event: "GeocodingFailed", city: "London", level: "WARN"}` — queryable by any field

The difference matters enormously when you're debugging at 2am with 10,000 log entries per second flowing through your system.

### What you'd add in production

The `cacheStats` query exposes hit/miss counts, but it's a snapshot in time and only survives process restarts. A real observability setup would include:

- **Prometheus + Grafana:** Prometheus scrapes metrics from the gateway every 15 seconds (requests/sec, error rate, cache hit rate, response percentiles). Grafana renders live dashboards. You'd add `prometheus-net.AspNetCore` to the .NET project.
- **OpenTelemetry:** The CNCF standard for distributed tracing. Hot Chocolate supports it via `.AddInstrumentation()`. Traces export to Jaeger or Zipkin and let you see exactly where time is spent per resolver.
- **Centralized logging:** Log output piped to a log aggregation system (ELK stack, Datadog, Loki+Grafana). Store logs for 30 days. Alert on error rate spikes.
- **Health endpoints:** `GET /health/live` (is the process alive?) and `GET /health/ready` (is it ready to receive traffic? Redis connected?) for load balancers and Kubernetes.

---

## 10. What This Could Become

The current project is a clear, focused demonstration of the gateway pattern. But the architecture is designed for growth. Here's what a realistic evolution looks like.

### Authentication and authorization

The gateway is the natural place to enforce identity. Add a middleware that:
1. Reads a JWT from the `Authorization: Bearer <token>` header
2. Verifies the signature against a public key
3. Extracts the user's identity and permissions
4. Attaches them to the request context

Resolvers can then check: *"Does this user have permission to query stock prices?"* Or: *"Is this user on the free tier? Limit their news results to 3."*

This is called **authorization at the resolver level**, and it's one of GraphQL's strengths — you can have field-level permission checks, not just endpoint-level.

### Subscriptions: real-time data

GraphQL supports **subscriptions** via WebSocket. Instead of polling every 30 seconds for a stock quote, the client subscribes:

```graphql
subscription {
  stockPrice(ticker: "AAPL") {
    price
    changePercent
  }
}
```

The gateway maintains a WebSocket connection with the client and pushes updates whenever the price changes. The data flow inverts: instead of the client asking "what's the price?", the gateway tells the client "the price just changed."

This requires a different architecture on the backend — typically a message queue (Redis Pub/Sub, Kafka, or RabbitMQ) that receives price updates from a feed, which the gateway subscribes to and fans out to connected clients.

### More upstream services

The current three services (weather, news, finance) are stateless REST APIs. The pattern extends to anything:

- **Databases:** A PostgreSQL resolver for user preferences and saved cities
- **gRPC services:** Internal microservices often communicate via gRPC; the gateway translates between GraphQL and gRPC
- **Other GraphQL APIs:** In a microservices architecture, each service might have its own GraphQL schema. A technique called **schema federation** (Apollo Federation, Hot Chocolate's Fusion) stitches them into one unified schema
- **Third-party SaaS:** Stripe for payments, Twilio for SMS, SendGrid for email — all can be wrapped as GraphQL resolvers

### Schema federation

This deserves its own mention because it's one of the most important patterns in large-scale GraphQL.

At small scale, one gateway with one schema is fine. But in a large organization, different teams own different parts of the data. The Payments team owns `Order` and `Invoice`. The User team owns `User` and `Profile`. The Inventory team owns `Product` and `Stock`.

Schema federation lets each team define their part of the schema independently:

```graphql
# Payments service schema
type Order @key(fields: "id") {
  id: ID!
  total: Float!
}

# User service schema  
type User @key(fields: "id") {
  id: ID!
  name: String!
  orders: [Order!]!  # references the Order type from Payments
}
```

The gateway stitches these together. A query like `user { name orders { total } }` is automatically routed: the User service handles `name`, the Payments service handles `orders { total }`, and the gateway assembles the result. Teams can deploy their services independently without touching the gateway.

This is how companies like Netflix, Airbnb, and Shopify use GraphQL at scale.

### Performance tuning: the N+1 problem

There's a famous performance trap in GraphQL called the **N+1 problem**. Imagine a query that fetches 10 news articles, each with an author:

```graphql
query {
  topHeadlines {
    title
    author {
      name
      bio
    }
  }
}
```

A naive implementation fetches 10 articles, then for each article makes a separate database call for the author. That's 1 + 10 = 11 queries. With 100 articles, it's 101 queries. This scales terribly.

The solution is **DataLoader** — a batching and caching utility developed by Facebook. Instead of fetching one author at a time, DataLoader collects all the author IDs requested during a single GraphQL execution, makes *one batched query* for all of them, and distributes the results.

Hot Chocolate has built-in DataLoader support. It's a non-trivial concept to implement correctly, and recognizing when you have an N+1 problem is a key skill.

### Deployment: from Docker to Kubernetes

The project ships with Docker Compose, which is excellent for local development and simple deployments. The natural evolution is Kubernetes.

Kubernetes adds:
- **Horizontal scaling:** Run 5 gateway replicas instead of 1; a load balancer distributes traffic
- **Health-based routing:** Automatically stops sending traffic to unhealthy replicas
- **Rolling deployments:** Deploy new versions without downtime by gradually replacing replicas
- **Resource limits:** Prevent one service from consuming all CPU/memory
- **Service discovery:** Services find each other by name, not hardcoded IP

The gateway's design anticipates this. Stateless resolvers (no in-memory request state), shared Redis (not per-process cache), and health endpoints make it Kubernetes-ready. The only thing you'd add is a Kubernetes `Deployment` and `Service` manifest.

---

## 11. The Mental Models to Carry Forward

Building and studying this project teaches patterns that appear in every large-scale system you'll ever work on. Here are the frameworks to internalize.

### The coupling-flexibility tradeoff

Every architectural decision is a trade between coupling and flexibility. Tight coupling is simple to build but expensive to change. Loose coupling is more complex to build but allows independent change.

The gateway loosens the coupling between clients and upstream services. Every service interface loosens the coupling between the gateway and its implementations. Every cache loosens the coupling between request frequency and upstream call frequency.

When you review code or design systems, always ask: *where is this coupled? What would I have to change if X changes?* The answers reveal the true cost of the design.

### Separation of concerns

Every class in this project has one job:
- `WeatherService` knows how to talk to Open-Meteo
- `CacheService` knows how to read and write Redis
- `Query` knows how to resolve GraphQL fields
- `Program.cs` knows how to wire everything together

No class crosses these boundaries. This is **separation of concerns**, and it's what makes the codebase navigable. When something breaks in the cache, you look in `CacheService.cs`. You don't search the entire codebase.

In your own code, when you find yourself reading a 500-line class, ask: what are the different *jobs* this class is doing? Can they be separated?

### Designing for failure

The question isn't *if* a dependency will fail — it's *when*. Systems that assume dependencies are reliable are systems with unpredictable outages.

This project treats failure as a first-class concern. Nullable return types, try/catch at service boundaries, fallback implementations, and a `NullCacheService` are all designed for the reality that things fail.

When you design a system, ask: *what happens when the database is slow? What happens when the cache is empty? What happens when the third-party API returns a 500?* Having answers to these questions before writing a line of code produces dramatically more reliable software.

### The cost of a request

Every time a user clicks something, a cascade of costs occurs: CPU cycles, network bytes, database reads, API quota consumed, money spent on infrastructure. Good engineers develop a sense for these costs.

When you see `await weather.GetCurrentAsync(city)`, you should think: *that's ~300ms and one request toward a 100/day quota.* When you see `await cache.GetAsync<WeatherData>(key)`, you should think: *that's ~1ms and free.*

This intuition — the rough cost of common operations — guides better decisions at design time, before you're firefighting a production cost spike.

### Interfaces are promises

An interface is a promise: "anything that implements this interface can be used wherever this interface is expected." `ICacheService` promises that it can `Get`, `Set`, and `GetStats`. `RedisCacheService` and `NullCacheService` both keep that promise with different implementations.

Designing your code around interfaces rather than concrete types is what enables testing, flexibility, and the O in SOLID (Open-Closed Principle: open for extension, closed for modification). You extend behavior by adding new implementations, not by modifying existing code.

---

### Closing thought

The GraphQL API Gateway is modest in scope but dense with real engineering patterns. The same cache-aside logic runs inside Google's infrastructure. The same `Task.WhenAll` pattern is how every high-performance backend system avoids wasting time on sequential I/O. The same abstraction layer thinking is how large engineering teams can change one service without breaking five others.

The gap between a side project and production infrastructure isn't usually the ideas — it's the depth of implementation, the operational tooling, and the experience of having been burned by the edge cases. Reading code like this, and asking *why* it was built this way, is how you accumulate that depth without having to make every mistake yourself.

Build it. Break it. Add the circuit breaker. Swap out a service. Write a test that mocks `IWeatherService`. See what happens when you deploy two instances and hit them with 1,000 requests. The patterns in this document will move from abstract to visceral.

That's engineering.
