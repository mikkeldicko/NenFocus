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