chrome.webNavigation.onBeforeNavigate.addListener(async (details) => {
    if (details.frameId !== 0) return;

    try {
        const url = new URL(details.url);
        const domain = url.hostname.replace(/^www\./, '');

        if (domain === "localhost") return;

        const response = await fetch(`http://localhost:5005/check?domain=${domain}`);
        const data = await response.json();

        if (data.blocked) {
            // 1. Report the attempt to your C# app
            fetch(`http://localhost:5005/report?domain=${domain}`);

            // 2. Redirect to your local "Bible Boy" page
            chrome.tabs.update(details.tabId, {
                url: chrome.runtime.getURL("blocked.html")
            });
        }
    } catch (error) {
        console.log("NenFocus app is not active or server is down.");
    }
});