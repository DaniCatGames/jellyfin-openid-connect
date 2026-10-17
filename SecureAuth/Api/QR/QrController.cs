using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Plugin.SecureAuth.Extensions;
using Jellyfin.Plugin.SecureAuth.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        string pairingCode = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(48));
        string url = Request.GetRequestBase() + "/AuthViews/QR/Approve?pairingCode=" + pairingCode;
        string svgQr = new SvgQRCode(QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q)).GetGraphic();

        string fingerprint = Convert.ToString(RandomNumberGenerator.GetInt32(0, 1000000)).PadLeft(6, '0');

        string flowCookie = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("qr_flow",
            flowCookie,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(5),
                Secure = Request.IsHttps,
            });

        stateManager.TryAdd(pairingCode,
            new TimedQrState(DateTime.UtcNow)
            {
                Cookie = flowCookie,
                DeviceId = deviceData.DeviceId,
                DeviceName = deviceData.DeviceName,
                AppName = deviceData.AppName,
                AppVersion = deviceData.AppVersion,
                Fingerprint = fingerprint,
                IP = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            });

        logger.LogInformation("Created QR code for {deviceName} ({appName} {appVersion})",
            deviceData.DeviceName,
            deviceData.AppName,
            deviceData.AppVersion);

        return Ok(new { svgQr, fingerprint, pairingCode });
    }

    /// <summary>
    ///     Check the status of the login.
    /// </summary>
    /// <param name="pairCode"></param>
    /// <param name="deviceId"></param>
    [HttpGet]
    [Route("Poll")]
    public ActionResult Poll([FromQuery] string pairCode, [FromQuery] string deviceId)
    {
        if (!stateManager.TryGetValue(pairCode, out TimedQrState timedState) || timedState.DeviceId != deviceId)
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
        if (!stateManager.TryGetValue(completionData.PairCode, out TimedQrState timedState)
            || timedState.State != QrCodeState.Approved || timedState.IsExpired()
            || timedState.DeviceId != completionData.DeviceId)
        {
            return BadRequest("Invalid or not approved state");
        }

        var authRequest = new AuthenticationRequest
        {
            UserId = timedState.UserId,
            App = timedState.AppName,
            AppVersion = timedState.AppVersion,
            DeviceId = timedState.DeviceId,
            DeviceName = timedState.DeviceName,
            RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        stateManager.TryRemove(completionData.PairCode, out _);

        AuthenticationResult result = await sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    ///     Cancel the QR code flow.
    /// </summary>
    /// <param name="pairCode">the pairing data</param>
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
    [Authorize]
    [HttpPost]
    [Route("Approve")]
    public async Task<ActionResult> Approve([FromQuery] string pairingCode, [FromQuery] bool action)
    {
        if (!stateManager.TryGetValue(pairingCode, out TimedQrState timedState)
            || timedState.State != QrCodeState.Pending)
        {
            return BadRequest("Invalid state");
        }

        AuthorizationInfo authorizationInfo =
            await authorizationContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        timedState.UserId = authorizationInfo.UserId;
        timedState.State = action ? QrCodeState.Approved : QrCodeState.Denied;
        return Ok();
    }

    /// <summary>
    ///     Allow the scanning device to get the device info.
    /// </summary>
    /// <param name="pairCode">the pairing code</param>
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
            timedState.IP,
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