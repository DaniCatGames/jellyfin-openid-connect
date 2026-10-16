#nullable enable
using System.IO;
using MediaBrowser.Model;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SecureAuth.Api;

/// <summary>
///     Serve views
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger{AuthViewsController}" /> interface.</param>
[ApiController]
[Route("[controller]")]
public class AuthViewsController(
    ILogger<AuthViewsController> logger
) : ControllerBase
{
    /// <summary>
    ///     Get a view by name.
    /// </summary>
    /// <param name="viewName">The name to fetch.</param>
    [HttpGet("{*viewName}")]
    public ActionResult GetView([FromRoute] string viewName)
    {
        if (!AuthViewRegistry.Instance.TryGet(viewName, out PluginPageInfo view))
        {
            logger.LogDebug("Requested view {View} is not registered", viewName);
            return NotFound("No matching view found");
        }

        Stream? stream = AuthViewRegistry.Instance.OpenStream(viewName);
        if (stream is null)
        {
            logger.LogError(
                "Failed to open embedded resource {Resource} for view {View}",
                view.EmbeddedResourcePath,
                viewName);
            return NotFound();
        }

        return File(stream, MimeTypes.GetMimeType(view.EmbeddedResourcePath));
    }
}