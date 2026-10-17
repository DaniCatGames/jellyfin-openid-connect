export class Credentials {
    constructor(key = "jellyfin_credentials") {
        this._c = JSON.parse(localStorage.getItem(key) || "{}");
        this._c.Servers ||= [];
    }
    credentials() {
        return this._c;
    }
}

export class ApiClient {
    constructor(serverAddress, appName, appVersion, deviceName, deviceId) {
        this._serverAddress = serverAddress;
        this._appName = appName;
        this._appVersion = appVersion;
        this._deviceName = deviceName;
        this._deviceId = deviceId;
        this._serverInfo = {};
        this._loggedIn = false;
    }

    serverAddress() {
        return this._serverAddress;
    }
    setAuthenticationInfo(token, userId) {
        this._serverInfo.AccessToken = token;
        this._serverInfo.UserId = userId;
        this._loggedIn = !!(token && userId);
    }
    getCurrentUserId() {
        return this._loggedIn ? this._serverInfo.UserId : null;
    }
    accessToken() {
        return this._loggedIn ? this._serverInfo.AccessToken : null;
    }

    getUrl(name, params) {
        if (!name) throw new Error("Url name cannot be empty");
        let url = this._serverAddress; // server must be set
        if (name.charAt(0) !== "/") url += "/";
        url += name;
        const qs =
            params &&
            Object.entries(params)
                .filter(([, v]) => v != null && v !== "")
                .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
                .join("&");
        return qs ? `${url}?${qs}` : url;
    }

    setRequestHeaders(headers) {
        headers["Authorization"] =
            `MediaBrowser Client="${this._appName}", Device="${this._deviceName}", DeviceId="${this._deviceId}", Version="${this._appVersion}", Token="${this.accessToken()}"`;
    }

    async fetch({ url, type = "GET", dataType, contentType, data, headers = {} }) {
        if (dataType === "json") headers["Accept"] = "application/json";
        this.setRequestHeaders(headers);
        const init = { method: type, credentials: "same-origin", headers };
        if (data != null) {
            init.body = typeof data === "string" ? data : JSON.stringify(data);
            headers["Content-Type"] = contentType || "application/json";
        }
        const resp = await fetch(url, init);
        if (resp.status >= 400) throw resp; // library rejects the Response
        return dataType === "json" || (resp.headers.get("Content-Type") || "").startsWith("application/json")
            ? resp.json()
            : resp.text();
    }

    getJSON(url) {
        return this.fetch({ url, type: "GET", dataType: "json" });
    }
}
