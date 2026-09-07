(() => {
    // Guards against running twice in the same document - the declarative content
    // script and the background script's existing-tab backfill can both land here.
    if (window.__nenFocusPollActive) return;
    window.__nenFocusPollActive = true;

    const domain = location.hostname.replace(/^www\./, '');

    const checkOnce = () => {
        // Relayed through background.js rather than fetched here directly - a content
        // script's fetch to localhost is attributed to this page's own origin, which
        // Chromium denies under Local Network Access regardless of server response
        // headers. The background script's fetch is unaffected, so it does the actual
        // check/report and messages the result back.
        chrome.runtime.sendMessage({ type: "nenFocusCheck", domain }, (response) => {
            // chrome.runtime.lastError fires if the service worker was mid-restart with
            // no listener yet, or the extension context was invalidated (e.g. reloaded).
            // Either way, just skip this tick - the next one retries.
            if (chrome.runtime.lastError) return;

            if (response?.blocked) {
                clearInterval(intervalId);
                location.replace(chrome.runtime.getURL("blocked.html"));
            }
        });
    };

    const intervalId = setInterval(checkOnce, 2000);
    checkOnce();
})();
