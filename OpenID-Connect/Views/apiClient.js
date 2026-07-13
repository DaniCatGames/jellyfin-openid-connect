import jellyfinApiclient from "./jellyfin-apiClient.esm.min.js";
import { getDeviceName, sleep } from "./shared.js";

window.jellyfinApiclient = jellyfinApiclient;
console.log(jellyfinApiclient);

// https://github.com/jellyfin/jellyfin-web/blob/9067b0e397cc8b38635d661ce86ddd83194f3202/src/scripts/clientUtils.js#L19-L76
export async function serverAddress({ basePath = "/web" }) {
    const apiClient = window.ApiClient;

    if (apiClient) {
        return Promise.resolve(apiClient.serverAddress());
    }

    const urls = [];

    const getViewUrl = (basePath) => {
        let url;
        const index = window.location.href.toLowerCase().lastIndexOf(basePath.toLowerCase());

        if (index != -1) {
            url = window.location.href.substring(0, index);
        } else {
            // Return nothing, let another method handle it
            url = undefined;
        }

        return url;
    };

    if (urls.length === 0) {
        // Otherwise use computed base URL
        let url;

        url = getViewUrl(basePath) ?? getViewUrl("/web") ?? window.location.origin;

        // Don't use bundled app URL (file:) as server URL
        if (url.startsWith("file:")) {
            return Promise.resolve();
        }

        urls.push(url);
    }

    console.debug("URL candidates:", urls);

    const promises = urls.map((url) => {
        return fetch(`${url}/System/Info/Public`)
            .then((resp) => {
                return {
                    url: url,
                    response: resp,
                };
            })
            .catch(() => {
                return Promise.resolve();
            });
    });

    return Promise.all(promises)
        .then((responses) => {
            responses = responses.filter((obj) => obj && obj.response.ok);
            return Promise.all(
                responses.map((obj) => {
                    return {
                        url: obj.url,
                        config: obj.response.json(),
                    };
                }),
            );
        })
        .then((configs) => {
            const selection = configs.find((obj) => !obj.config.StartupWizardCompleted) || configs[0];
            return Promise.resolve(selection?.url);
        })
        .catch((error) => {
            console.log(error);
            return Promise.resolve();
        });
}

function getDeviceId() {
    return localStorage.getItem("_deviceId2");
}

async function awaitLocalStorage() {
    while (
        localStorage.getItem("_deviceId2") == null ||
        localStorage.getItem("jellyfin_credentials") == null ||
        JSON.parse(localStorage.getItem("jellyfin_credentials"))["Servers"][0]["Id"] == null
    ) {
        // If localStorage isn't initialized yet, try again.
        await sleep(100);
    }
}

await awaitLocalStorage();

// Fetch credentials

var credentials = new jellyfinApiclient.Credentials();

var server = await serverAddress({ basePath: "/OpenIDConnectViews" });

const infoResponse = await fetch(`${server}/System/Info/Public`);
const serverInfo = await infoResponse.json();
const activeServerId = serverInfo.Id;

console.log({ server: server });
var deviceId = getDeviceId();
var appName = "Jellyfin%20Web";
var appVersion = serverInfo.Version;
var capabilities = {};

const current_server = credentials.credentials().Servers.find((e) => e.Id === activeServerId) || {};

var localApiClient = new jellyfinApiclient.ApiClient(server, appName, appVersion, getDeviceName(), deviceId);

localApiClient.enableAutomaticBitrateDetection = false;

localApiClient.setAuthenticationInfo(current_server.AccessToken, current_server.UserId);

var connections = new jellyfinApiclient.ConnectionManager(
    credentials,
    appName,
    appVersion,
    getDeviceName(),
    deviceId,
    capabilities,
);

//fix old authorization headers so the server accepts our requests
localApiClient.setRequestHeaders = function (headers) {
    const token = current_server.AccessToken;

    const parts = [
        `Client="${appName}"`,
        `Device="${getDeviceName()}"`,
        `DeviceId="${deviceId}"`,
        `Version="${appVersion}"`,
        `Token="${token}"`,
    ];

    headers["Authorization"] = `MediaBrowser ${parts.join(", ")}`;
};

connections.addApiClient(localApiClient);

window.ApiClient = localApiClient;

export default localApiClient;
