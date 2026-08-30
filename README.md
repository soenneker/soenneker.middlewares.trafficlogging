# Soenneker.Middlewares.TrafficLogging
[![](https://img.shields.io/nuget/v/soenneker.middlewares.trafficlogging.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.middlewares.trafficlogging/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.middlewares.trafficlogging/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.middlewares.trafficlogging/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.middlewares.trafficlogging.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.middlewares.trafficlogging/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.middlewares.trafficlogging/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.middlewares.trafficlogging/actions/workflows/codeql.yml)

Adds structured ASP.NET Core request and response logging with sensitive payload capture disabled by default.

## Installation

```bash
dotnet add package Soenneker.Middlewares.TrafficLogging
```

## Registration

No service registration is required:

```csharp
using Soenneker.Middlewares.TrafficLogging.Registrars;

app.UseTrafficLogging();
app.MapControllers();
```

Place it before endpoints and other middleware whose responses should be logged. WebSocket requests are passed through without traffic logging. If Information logging is disabled for `Soenneker.Middlewares.TrafficLogging.TrafficLoggingMiddleware`, the middleware adds no request or response capture.

## Default output

The default log records method, scheme, host, path, response status, and known body length. It does not log query strings, headers, request bodies, or response bodies.

Enable additional fields individually through configuration:

```json
{
  "TrafficLogging": {
    "LogHeaders": true,
    "LogQueryString": false,
    "LogRequestBody": false,
    "LogResponseBody": false
  }
}
```

Headers whose names indicate authorization, cookies, API keys, secrets, or tokens are always replaced with `[REDACTED]`. Header values are capped at 512 characters.

Text-like request and response bodies are captured only when their respective setting is enabled, and only the first 32 KiB is logged. Request body capture is skipped for GET, HEAD, DELETE, and TRACE, for declared empty bodies, for declared bodies over 5 MiB, and for non-text content types. Response capture forwards writes immediately and retains only the capped prefix, so it does not buffer the complete response or delay streaming output.

## Security considerations

Opt-in data can still contain passwords, access tokens, personal information, and application-specific secret headers that a name-based redactor cannot identify. Query strings and JSON/form bodies are especially high risk. Enable them only in a controlled environment with appropriate log access, retention, and deletion policies.

Captured text is sanitized for control characters to prevent line-oriented log injection. Host names, paths, header values, and payload text remain attacker-controlled data and should not be used to construct log templates or security decisions.
