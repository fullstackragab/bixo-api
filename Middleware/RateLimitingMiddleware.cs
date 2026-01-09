using System.Collections.Concurrent;
using System.Net;

namespace bixo_api.Middleware;

/// <summary>
/// Simple IP-based rate limiting middleware for public endpoints.
/// Uses in-memory storage - suitable for single-instance deployments.
/// For multi-instance deployments, consider Redis-based rate limiting.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // In-memory store: IP -> (request count, window start)
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _requestCounts = new();

    // Configuration
    private const int MAX_REQUESTS_PER_HOUR = 5;
    private const int WINDOW_MINUTES = 60;

    // Paths to rate limit
    private static readonly string[] RateLimitedPaths = new[]
    {
        "/api/shortlists/public/request"
    };

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Only rate limit specific public endpoints
        if (!ShouldRateLimit(path))
        {
            await _next(context);
            return;
        }

        var clientIp = GetClientIp(context);
        var now = DateTime.UtcNow;

        // Clean up old entries periodically (simple approach)
        if (_requestCounts.Count > 10000)
        {
            CleanupOldEntries(now);
        }

        var entry = _requestCounts.GetOrAdd(clientIp, _ => new RateLimitEntry
        {
            Count = 0,
            WindowStart = now
        });

        bool rateLimitExceeded = false;
        int retryAfterMinutes = 0;

        lock (entry)
        {
            // Reset window if expired
            if ((now - entry.WindowStart).TotalMinutes >= WINDOW_MINUTES)
            {
                entry.Count = 0;
                entry.WindowStart = now;
            }

            entry.Count++;

            if (entry.Count > MAX_REQUESTS_PER_HOUR)
            {
                rateLimitExceeded = true;
                retryAfterMinutes = (int)(WINDOW_MINUTES - (now - entry.WindowStart).TotalMinutes);
            }
        }

        if (rateLimitExceeded)
        {
            _logger.LogWarning("Rate limit exceeded for IP {ClientIp} on path {Path}", clientIp, path);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfterMinutes.ToString();

            var response = new
            {
                success = false,
                message = "Rate limit exceeded. Please try again later.",
                retryAfterMinutes
            };

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(response);
            return;
        }

        await _next(context);
    }

    private static bool ShouldRateLimit(string path)
    {
        return RateLimitedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetClientIp(HttpContext context)
    {
        // Check for forwarded IP (behind proxy/load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static void CleanupOldEntries(DateTime now)
    {
        var keysToRemove = _requestCounts
            .Where(kvp => (now - kvp.Value.WindowStart).TotalMinutes > WINDOW_MINUTES * 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _requestCounts.TryRemove(key, out _);
        }
    }

    private class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime WindowStart { get; set; }
    }
}

/// <summary>
/// Extension methods for adding rate limiting middleware.
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UsePublicEndpointRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
