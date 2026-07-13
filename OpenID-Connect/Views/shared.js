export const getDeviceName = function () {
    const ua = navigator.userAgent;
    if (ua.includes("Firefox")) return "Firefox";
    if (ua.includes("Chrome")) return "Chrome";
    if (ua.includes("Safari")) return "Safari";
    return "Web Browser";
};

export const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
