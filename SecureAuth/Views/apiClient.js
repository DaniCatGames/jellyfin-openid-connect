import * as api from "./api.js";
import { getDeviceName, sleep } from "./shared.js";

window.jellyfinApiclient = api;

export async function serverAddress() {
    const apiClient = window.ApiClient;

    if (apiClient) {
        return apiClient.serverAddress();
    }

    const index = window.location.pathname.indexOf("/AuthViews/");

    return index !== -1
        ? window.location.origin + window.location.pathname.substring(0, index)
        : window.location.origin;
}

function getDeviceId() {
    let id = localStorage.getItem("_deviceId2");
    if (!id) {
        id = crypto.randomUUID();
        localStorage.setItem("_deviceId2", id);
    }
    return id;
}

async function awaitLocalStorage() {
    const deadline = Date.now() + 2000;
    while (localStorage.getItem("_deviceId2") == null && Date.now() < deadline) {
        await sleep(100);
    }
}

await awaitLocalStorage();

// Fetch credentials

const credentials = new api.Credentials();

const server = await serverAddress();
if (!server) {
    throw new Error("Could not determine the Jellyfin server address");
}

const infoResponse = await fetch(`${server}/System/Info/Public`);
if (!infoResponse.ok) {
    throw new Error(`Could not reach the Jellyfin server (${infoResponse.status})`);
}

const serverInfo = await infoResponse.json();
const activeServerId = serverInfo.Id;

console.log({ server: server });
const deviceId = getDeviceId();
const appName = "Jellyfin Web";
const appVersion = serverInfo.Version;

const current_server = credentials.credentials().Servers.find((e) => e.Id === activeServerId) || {};

const localApiClient = new api.ApiClient(server, appName, appVersion, getDeviceName(), deviceId);

localApiClient.setAuthenticationInfo(current_server.AccessToken, current_server.UserId);

window.ApiClient = localApiClient;

export default localApiClient;
