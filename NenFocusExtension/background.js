// Content scripts declared in the manifest only auto-inject on future page loads.
// Tabs already open when the extension installs/updates/starts never get poll.js
// on their own, so back-fill it into every matching tab that already exists.
const injectPollIntoExistingTabs = () => {
    chrome.tabs.query({ url: ["http://*/*", "https://*/*"] }, (tabs) => {
        for (const tab of tabs) {
            chrome.scripting.executeScript({
                target: { tabId: tab.id },
                files: ["poll.js"]
            }).catch(() => { });
        }
    });
};

chrome.runtime.onInstalled.addListener(injectPollIntoExistingTabs);
chrome.runtime.onStartup.addListener(injectPollIntoExistingTabs);

// Shared by onBeforeNavigate below and the onMessage relay for poll.js. Fetches to
// localhost only succeed from this privileged extension context - a content script's
// fetch to the same URL is attributed to the page's own origin, which recent Chromium
// versions deny outright under Local Network Access ("Permission was denied for this
// request to access the `loopback` address space") before any CORS response header is
// even consulted. So poll.js can't call this endpoint directly; it has to ask us to.
const checkAndReport = async (domain) => {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 500);

    try {
        const response = await fetch(`http://localhost:5005/check?domain=${domain}`, {
            signal: controller.signal
        });
        clearTimeout(timeoutId);
        const data = await response.json();

        if (data.blocked) {
            fetch(`http://localhost:5005/report?domain=${domain}`).catch(() => { });
        }
        return !!data.blocked;
    } catch (error) {
        // If the C# app is closed, this will trigger.
        clearTimeout(timeoutId);
        return false;
    }
};

chrome.webNavigation.onBeforeNavigate.addListener(async (details) => {
    // Only handle the main frame (top-level navigation)
    if (details.frameId !== 0) return;

    try {
        const url = new URL(details.url);

        // Safety: Don't check internal chrome pages or our own extension pages
        if (url.protocol.startsWith('chrome') || url.hostname === "localhost") return;

        const domain = url.hostname.replace(/^www\./, '');
        const blocked = await checkAndReport(domain);

        if (blocked) {
            chrome.tabs.update(details.tabId, {
                url: chrome.runtime.getURL("blocked.html")
            });
        }
    } catch (error) {
        // If the C# app is closed, this will trigger.
        // We do nothing and let the browser load naturally.
        console.log("NenFocus check failed:", error.message);
    }
});

// poll.js (content script) relays its periodic already-open-tab check through here
// instead of fetching localhost itself - see checkAndReport's comment above for why.
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type !== "nenFocusCheck") return false;

    checkAndReport(message.domain).then((blocked) => sendResponse({ blocked }));
    return true; // keep the message channel open for the async sendResponse above
});
