using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;

namespace Jellyfin.Plugin.OpenIDConnect.Views;

/// <summary>
///     A helper class to return HTML for the client's auth flow.
/// </summary>
public static class WebResponse
{
    private static string _template;

    private static string GetTemplate()
    {
        if (_template != null)
        {
            return _template;
        }

        var assembly = Assembly.GetExecutingAssembly();
        const string flowhtml = "Jellyfin.Plugin.OpenIDConnect.Views.flow.html"; // Adjust namespace if necessary

        using Stream stream = assembly.GetManifestResourceStream(flowhtml);
        if (stream == null)
        {
            throw new FileNotFoundException("Can't find the flow template");
        }

        using var reader = new StreamReader(stream);
        _template = reader.ReadToEnd();

        return _template;
    }


    /// <summary>
    ///     A generator for the web response that incorporates the data from the server.
    /// </summary>
    /// <param name="data">The data of the auth flow. Is a state ID for OpenID.</param>
    /// <param name="provider">The name of the provider to callback to.</param>
    /// <param name="baseUrl">The base URL of the Jellyfin installation.</param>
    /// <param name="isLinking">Whether or not this request is to link accounts (Rather than authenticate).</param>
    /// <returns>A string with the HTML to serve to the client.</returns>
    public static string Generator(string data, string provider, string baseUrl, bool isLinking = false)
    {
        // Strip out the protocol (http:// or https://) and convert the domain to Punycode
        var idnMapping = new IdnMapping();
        int protocolSeparatorIndex = baseUrl.IndexOf("//", StringComparison.InvariantCulture);
        string protocol = baseUrl.Substring(0, protocolSeparatorIndex + 2);
        string domain = baseUrl.Substring(protocolSeparatorIndex + 2);
        string punycodeDomain = idnMapping.GetAscii(domain);
        string punycodeBaseUrl = protocol + punycodeDomain;

        return GetTemplate()
            .Replace("CS_BASEURL", punycodeBaseUrl)
            .Replace("CS_PROVIDER", provider)
            .Replace("CS_DATA", data)
            .Replace("CS_ISLINKING", isLinking.ToString().ToLower());
    }

    /// <summary>
    ///     A generator for the web response that shows the result of a testing authentication flow.
    /// </summary>
    /// <param name="provider">The name of the provider that was tested</param>
    /// <param name="claims">The claims from the test authentication</param>
    /// <returns>The thml to serve to the client</returns>
    public static string GenerateHtmlTestingPage(string provider, IEnumerable<Claim> claims)
    {
        // Build table rows from returned IdP claims
        string rows = string.Join("",
            claims.Select(c =>
                $"<tr><td style='padding: 10px; border-bottom: 1px solid #333; font-weight: bold; color: var(--emphasis, #00a4dc);'>{c.Type}</td>"
                +
                $"<td style='padding: 10px; border-bottom: 1px solid #333; font-family: monospace; word-break: break-all;'>{c.Value}</td></tr>"));

        string htmlOutput = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>OIDC Connection Test - {provider}</title>
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
                        <h2>OIDC Test Authentication Successful! <span class='badge'>{provider}</span></h2>
                        
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
        return htmlOutput;
    }
}