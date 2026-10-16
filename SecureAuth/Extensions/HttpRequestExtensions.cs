using System;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.SecureAuth.Extensions;

/// <summary>
///     Extension methods for <see cref="HttpRequest" />.
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>
    ///     Quick method to get the base URL of the request.
    /// </summary>
    public static string GetRequestBase(this HttpRequest request, bool useHttp = false, int? portOverride = null)
    {
        int requestPort = portOverride ?? request.Host.Port ?? -1;

        if ((requestPort, request.Scheme) is (80, "http") or (443, "https"))
        {
            requestPort = -1;
        }

        return new UriBuilder
        {
            Scheme = useHttp ? "http" : "https",
            Host = request.Host.Host,
            Port = requestPort,
            Path = request.PathBase,
        }.ToString().TrimEnd('/');
    }
}