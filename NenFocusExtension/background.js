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

chrome.webNavigation.onBeforeNavigate.addListener(async (details) => {
    // Only handle the main frame (top-level navigation)
    if (details.frameId !== 0) return;

    try {
        const url = new URL(details.url);

        // Safety: Don't check internal chrome pages or our own extension pages
        if (url.protocol.startsWith('chrome') || url.hostname === "localhost") return;

        const domain = url.hostname.replace(/^www\./, '');

        // Use a controller to timeout the fetch if the C# app is slow
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 500);

        const response = await fetch(`http://localhost:5005/check?domain=${domain}`, {
            signal: controller.signal
        });

        clearTimeout(timeoutId);
        const data = await response.json();

        if (data.blocked) {
            // Redirect the tab immediately
            chrome.tabs.update(details.tabId, {
                url: chrome.runtime.getURL("blocked.html")
            });

            // Optional: async report (no need to await)
            fetch(`http://localhost:5005/report?domain=${domain}`).catch(() => { });
        }
    } catch (error) {
        // If the C# app is closed, this will trigger. 
        // We do nothing and let the browser load naturally.
        console.log("NenFocus check failed:", error.message);
    }
});