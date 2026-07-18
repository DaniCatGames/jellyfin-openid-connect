using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenIDConnect.Services;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OpenIDConnect.Api;

/// <summary>
///     The sso api controller.
/// </summary>
[ApiController]
[Route("OpenIDConnect")]
public class LinkController : ControllerBase
{
    private readonly IAuthorizationContext _authContext;
    private readonly ILinkManager _linkManager;
    private readonly IStateManager _stateManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenIDConnectController" /> class.
    /// </summary>
    /// <param name="authContext">Instance of the <see cref="IAuthorizationContext" /> interface.</param>
    /// <param name="linkManager">Instance of the <see cref="ILinkManager" /> interface.</param>
    /// <param name="stateManager">Instance of the <see cref="IStateManager" /> interface.</param>
    public LinkController(IAuthorizationContext authContext, ILinkManager linkManager, IStateManager stateManager)
    {
        _authContext = authContext;
        _linkManager = linkManager;
        _stateManager = stateManager;
    }

    /// <summary>
    ///     Create a canonical link for a given user. Must be performed by the user being changed, or admin.
    /// </summary>
    /// <param name="provider">The name of the provider to link to a jellyfin account.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to link to the provider.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpPost("Link/{provider}/{jellyfinUserId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> CreateLink(
        [FromRoute] string provider,
        [FromRoute] Guid jellyfinUserId,
        [FromBody] AuthResponse authResponse)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to link SSO providers.");
        }

        if (!OpenIDConnect.Instance.Configuration.OidConfigs.TryGetValue(provider, out _))
        {
            return BadRequest("No matching provider found");
        }

        if (!_stateManager.TryGetValue(authResponse.Data, out TimedAuthorizeState timedState))
        {
            return Problem("State not found");
        }

        if (!_stateManager.IsValid(timedState))
        {
            return Problem("State is not valid");
        }

        _stateManager.TryRemove(authResponse.Data, out _);

        if (!_linkManager.TryCreateLink(provider, timedState.Sub, jellyfinUserId))
        {
            return BadRequest("Error. Check server logs.");
        }

        return NoContent();
    }

    /// <summary>
    ///     Unregisters a given mapping from id within provider to user.
    /// </summary>
    /// <param name="provider">The name of the provider from which the link should be removed.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to unlink from the provider.</param>
    /// <param name="sub">The sub of the user in the IdP to unlink.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpDelete("Link/{provider}/{jellyfinUserId}/{sub}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> DeleteCanonicalLink(
        [FromRoute] string provider,
        [FromRoute] Guid jellyfinUserId,
        [FromRoute] string sub)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Current user is not allowed to unlink SSO providers for user ID.");
        }

        if (!_linkManager.TryGetLink(provider, sub, out Guid linkedId))
        {
            return BadRequest("No matching link found");
        }

        ;

        if (linkedId != jellyfinUserId)
        {
            return Conflict("Jellyfin User ID does not match the user id registered to that IdP sub.");
        }

        return _linkManager.TryDeleteLink(provider, sub) ? NoContent() : BadRequest("Error. Check server logs.");
    }

    /// <summary>
    ///     Gets all the canonical links for a user.
    /// </summary>
    /// <param name="jellyfinUserId">The user ID within jellyfin for which to return the links.</param>
    /// <returns>A dictionary of provider : link mappings.</returns>
    [Authorize]
    [HttpGet("Links/{jellyfinUserId}")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<SerializableDictionary<string, IEnumerable<string>>>> GetLinksByUser(
        Guid jellyfinUserId)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Non-admin is not allowed to query other user's mappings.");
        }

        return _linkManager.GetLinksByUser(jellyfinUserId);
    }
}