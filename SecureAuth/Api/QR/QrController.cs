using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Plugin.SecureAuth.Extensions;
using Jellyfin.Plugin.SecureAuth.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Jellyfin.Plugin.SecureAuth.Api.QR;

/// <summary>
/// </summary>
[ApiController]
[Route("SecureAuth/QR")]
public class QrController(
    ILogger<QrController> logger,
    IStateManager<TimedQrState> stateManager,
    IAuthorizationContext authorizationContext,
    ISessionManager sessionManager) : ControllerBase
{
    //TODO: Add config options for QR login flow

    /// <summary>
    ///     Create a new QR code to scan
    /// </summary>
    [HttpPost]
    [Route("Create")]
    public ActionResult Create([FromBody] DeviceData deviceData)
    {
        stateManager.Invalidate();

        string pairingCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(128));
        string url = Request.GetRequestBase() + "/SecureAuth/QR/Approve?pairingCode" + pairingCode;
        string svgQr = new SvgQRCode(QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q)).GetGraphic();

        string fingerprint = Convert.ToString(RandomNumberGenerator.GetInt32(0, 1000000)).PadLeft(6, '0');

        stateManager.TryAdd(pairingCode,
            new TimedQrState(DateTime.UtcNow)
            {
                DeviceId = deviceData.DeviceId,
                DeviceName = deviceData.DeviceName,
                AppName = deviceData.AppName,
                AppVersion = deviceData.AppVersion,
                Fingerprint = fingerprint,
            });

        logger.LogInformation("Created QR code for {deviceName} ({appName} {appVersion})",
            deviceData.DeviceName,
            deviceData.AppName,
            deviceData.AppVersion);

        return Ok(new { svgQr, fingerprint });
    }

    /// <summary>
    /// </summary>
    /// <param name="pairCode"></param>
    [HttpGet]
    [Route("Poll")]
    public ActionResult Poll([FromQuery] string pairCode)
    {
        if (!stateManager.TryGetValue(pairCode, out TimedQrState timedState))
        {
            return BadRequest("Invalid state");
        }

        if (timedState.IsExpired())
        {
            stateManager.TryRemove(pairCode, out _);
            return Ok(QrCodeState.Expired);
        }

        return Ok(timedState.State);
    }

    /// <summary>
    ///     Called by the authenticating device to complete the login and receive credentials.
    /// </summary>
    /// <param name="completionData"></param>
    [HttpPost]
    [Route("Complete")]
    public async Task<ActionResult> Complete([FromBody] CompletionData completionData)
    {
        if (!stateManager.TryGetValue(completionData.PairCode, out TimedQrState timedState))
        {
            return BadRequest("Invalid state");
        }

        AuthorizationInfo authorizationInfo =
            await authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        Guid userId = authorizationInfo.UserId;


        var authRequest = new AuthenticationRequest
        {
            UserId = userId,
            App = timedState.AppName,
            AppVersion = timedState.AppVersion,
            DeviceId = timedState.DeviceId,
            DeviceName = timedState.DeviceName,
            RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        AuthenticationResult result = await sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    ///     Cancel the QR code flow.
    /// </summary>
    /// <param name="pairCode"></param>
    [HttpPost]
    [Route("Cancel")]
    public ActionResult Cancel([FromQuery] string pairCode)
    {
        stateManager.TryRemove(pairCode, out _);
        return Ok();
    }

    /// <summary>
    ///     Allow the scanning device to approve/deny the login.
    /// </summary>
    /// <param name="approvalData">The pairing id and action</param>
    [Authorize]
    [HttpPost]
    [Route("Approve")]
    public ActionResult Approve([FromBody] ApprovalData approvalData)
    {
        stateManager.Invalidate();
        if (!stateManager.TryGetValue(approvalData.PairCode, out TimedQrState timedState))
        {
            return BadRequest("Invalid state");
        }

        timedState.State = approvalData.Action;
        return Ok();
    }

    /// <summary>
    ///     Allow the scanning device to get the device info.
    /// </summary>
    /// <param name="pairCode"></param>
    [HttpPost]
    [Route("Info")]
    public ActionResult Info([FromQuery] string pairCode)
    {
        if (!stateManager.TryGetValue(pairCode, out TimedQrState timedState))
        {
            return BadRequest("Invalid state");
        }

        return Ok(new
        {
            timedState.DeviceName,
            timedState.AppVersion,
            timedState.AppName,
            timedState.Fingerprint,
        });
    }
}

/// <summary>
///     The data the client should pass send to get a QR code
/// </summary>
/// <param name="DeviceId">The device ID of the client.</param>
/// <param name="DeviceName">The device name of the client.</param>
/// <param name="AppName">The app name of the client.</param>
/// <param name="AppVersion">The app version of the client.</param>
public record DeviceData(string DeviceId, string DeviceName, string AppName, string AppVersion);

/// <summary>
///     The data the client should pass send to complete the login
/// </summary>
/// <param name="DeviceId">The device ID of the client.</param>
/// <param name="PairCode">The pair code of the flow.</param>
public record CompletionData(string DeviceId, string PairCode);

/// <summary>
///     The data the client should pass send to complete the login
/// </summary>
/// <param name="Action">The action to take (Approve/Deny).</param>
/// <param name="PairCode">The pair code of the flow.</param>
public record ApprovalData(string PairCode, QrCodeState Action);