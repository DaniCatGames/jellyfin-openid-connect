using System;
using System.Globalization;
using System.IO;
using System.Reflection;

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
            .Replace("<<<BASEURL>>>", punycodeBaseUrl)
            .Replace("<<<PROVIDER>>>", provider)
            .Replace("<<<DATA>>>", data)
            .Replace("<<<ISLINKING>>>", isLinking.ToString().ToLower());
    }
}