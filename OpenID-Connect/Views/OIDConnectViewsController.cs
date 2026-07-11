using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDConnect.Views;

/// <summary>
///     The sso views controller.
/// </summary>
[ApiController]
[Route("[controller]")]
public class OIDConnectViewsController : ControllerBase
{
    private readonly IAuthorizationContext _authContext;
    private readonly ILogger<OIDConnectViewsController> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OIDConnectViewsController" /> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SSOViewsController}" /> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager" /> interface.</param>
    /// <param name="authContext">Instance of the <see cref="IAuthorizationContext" /> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager" /> interface.</param>
    public OIDConnectViewsController(
        ILogger<OIDConnectViewsController> logger,
        ISessionManager sessionManager,
        IUserManager userManager,
        IAuthorizationContext authContext)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _authContext = authContext;
        _logger = logger;
        _logger.LogInformation("SSO Views Controller initialized");
    }

    private ActionResult ServeView(string viewName)
    {
        IEnumerable<PluginPageInfo> pages = null;
        if (OIDConnect.Instance == null)
        {
            return BadRequest("No plugin instance found");
        }

        pages = OIDConnect.Instance.GetViews();

        PluginPageInfo view = pages.FirstOrDefault(pageInfo => pageInfo.Name == viewName, null);

        if (view == null)
        {
            return NotFound("No matching view found");
        }
#nullable enable
        Stream? stream = OIDConnect.Instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

        if (stream == null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
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