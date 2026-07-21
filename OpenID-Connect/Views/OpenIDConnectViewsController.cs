using System.IO;
using System.Linq;
using MediaBrowser.Model;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenIDConnect.Views;

/// <summary>
///     The sso views controller.
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger{OpenIDConnectViewsController}" /> interface.</param>
[ApiController]
[Route("[controller]")]
public class OpenIDConnectViewsController(
    ILogger<OpenIDConnectViewsController> logger
) : ControllerBase
{
    private ActionResult ServeView(string viewName)
    {
        if (OpenIDConnect.Instance == null)
        {
            return BadRequest("No plugin instance found");
        }

        PluginPageInfo view = OpenIDConnect.Instance.GetViews()
            .FirstOrDefault(pageInfo => pageInfo.Name == viewName, null);

        if (view == null)
        {
            return NotFound("No matching view found");
        }
#nullable enable
        Stream? stream = OpenIDConnect.Instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

        if (stream == null)
        {
            logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return NotFound();
        }
#nullable disable
        return File(stream, MimeTypes.GetMimeType(view.EmbeddedResourcePath));
    }

    /// <summary>
    ///     Gets a html view.
    /// </summary>
    /// <param name="viewName">The name of the view / asset to fetch.</param>
    /// <returns>The html view with the specified name.</returns>
    [HttpGet("{viewName}")]
    public ActionResult GetView([FromRoute] string viewName)
    {
        return ServeView(viewName);
    }
}