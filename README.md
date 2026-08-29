[![](https://img.shields.io/nuget/v/soenneker.middlewares.trafficlogging.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.middlewares.trafficlogging/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.middlewares.trafficlogging/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.middlewares.trafficlogging/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.middlewares.trafficlogging.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.middlewares.trafficlogging/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.middlewares.trafficlogging/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.middlewares.trafficlogging/actions/workflows/codeql.yml)

# Soenneker.Middlewares.TrafficLogging

Middleware that logs the full HTTP request and response, including headers and body, using buffered memory streams.

## Install

```bash
dotnet add package Soenneker.Middlewares.TrafficLogging
```

## Quick start

```csharp
using Soenneker.Middlewares.TrafficLogging.Registrars;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var result = services.AddTrafficLoggingMiddlewareAsSingleton();
```

Adds `ITrafficLoggingMiddleware` as a singleton service. Set `TrafficLogging:EnableHeaderRedaction` in configuration to false to disable redaction (default is true).

## What you get

- `ITrafficLoggingMiddleware` — Middleware that logs the full HTTP request and response, including headers and body, using buffered memory streams.
- `TrafficLoggingMiddlewareRegistrar` — Middleware that logs the full HTTP request and response, including headers and body, using buffered memory streams.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `TrafficLoggingMiddlewareRegistrar.AddTrafficLoggingMiddlewareAsSingleton(services)` | Adds `ITrafficLoggingMiddleware` as a singleton service. Set `TrafficLogging:EnableHeaderRedaction` in configuration to false to disable redaction (default is true). | The same service collection, so additional registrations can be chained. |
| `TrafficLoggingMiddlewareRegistrar.UseTrafficLogging(builder)` | Adds traffic logging for each request. Be careful! This logs the full HTTP request and response, including headers and body, using buffered memory streams. Be sure to register first via AddTrafficLoggingMiddlewareAsSingleton(). | The same builder instance, so additional classes or variants can be chained. |
