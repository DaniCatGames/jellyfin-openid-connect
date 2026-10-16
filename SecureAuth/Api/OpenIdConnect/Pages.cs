using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;

namespace Jellyfin.Plugin.SecureAuth.Api.OpenIdConnect;

/// <summary>
///     Renders the OIDC html pages
/// </summary>
public static class Pages
{
    /// <summary>
    ///     Render the auth flow page.
    /// </summary>
    /// <param name="data">The data of the auth flow.</param>
    /// <param name="provider">The name of the provider</param>
    /// <param name="baseUrl">The base URL</param>
    /// <param name="isLinking">Whether this request is to link accounts.</param>
    public static string RenderFlowPage(string data, string provider, string baseUrl, bool isLinking = false)
    {
        // Strip out the protocol (http:// or https://) and convert the domain to Punycode
        var idnMapping = new IdnMapping();
        int protocolSeparatorIndex = baseUrl.IndexOf("//", StringComparison.InvariantCulture);
        string protocol = baseUrl[..(protocolSeparatorIndex + 2)];
        string domain = baseUrl[(protocolSeparatorIndex + 2)..];
        string punycodeBaseUrl = protocol + idnMapping.GetAscii(domain);

        return AuthViewRegistry.Instance.ReadAllText("OIDC/flow.html")
            .Replace("CS_BASEURL", punycodeBaseUrl)
            .Replace("CS_PROVIDER", provider)
            .Replace("CS_DATA", data)
            .Replace("CS_ISLINKING", isLinking ? "true" : "false");
    }

    /// <summary>
    ///     Renders the result page of a test authentication flow.
    /// </summary>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="claims">The claims.</param>
    public static string RenderTestPage(string provider, IEnumerable<Claim> claims)
    {
        string rows = string.Join(
            string.Empty,
            claims.Select(c =>
                $"<tr><td style='padding: 10px; border-bottom: 1px solid #333; font-weight: bold; color: var(--emphasis, #00a4dc);'>{WebUtility.HtmlEncode(c.Type)}</td>"
                + $"<td style='padding: 10px; border-bottom: 1px solid #333; font-family: monospace; word-break: break-all;'>{WebUtility.HtmlEncode(c.Value)}</td></tr>"));

        return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>OIDC Connection Test - {WebUtility.HtmlEncode(provider)}</title>
                    <style>
                        body {{ background: #101010; color: #d1cfce; font-family: sans-serif; padding: 24px; line-height: 1.5; }}
                        .container {{ max-width: 800px; margin: 0 auto; }}
                        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; background: #1c1c1c; border: 1px solid #333; border-radius: 6px; overflow: hidden; }}
                        th {{ background: #252525; padding: 12px; text-align: left; border-bottom: 2px solid #444; color: #fff; }}
                        .badge {{ background: #00a4dc; color: white; padding: 4px 8px; border-radius: 4px; font-size: 0.85em; font-weight: bold; vertical-align: middle; }}
                        .btn-close {{ background: #252525; color: white; border: 1px solid #444; padding: 10px 20px; border-radius: 4px; cursor: pointer; font-weight: bold; margin-top: 20px; }}
                        .btn-close:hover {{ background: #333; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <h2>OIDC Test Authentication Successful! <span class='badge'>{WebUtility.HtmlEncode(provider)}</span></h2>

                        <table>
                            <thead>
                                <tr><th>Claim Type / Path</th><th>Returned Value</th></tr>
                            </thead>
                            <tbody>
                                {rows}
                            </tbody>
                        </table>
                    </div>
                </body>
                </html>";
    }
}