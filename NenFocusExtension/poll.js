(() => {
    // Guards against running twice in the same document - the declarative content
    // script and the background script's existing-tab backfill can both land here.
    if (window.__nenFocusPollActive) return;
    window.__nenFocusPollActive = true;

    const domain = location.hostname.replace(/^www\./, '');

    const checkOnce = async () => {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 500);

        try {
            const response = await fetch(`http://localhost:5005/check?domain=${domain}`, {
                signal: controller.signal
            });
            clearTimeout(timeoutId);
            const data = await response.json();

            if (data.blocked) {
                clearInterval(intervalId);
                fetch(`http://localhost:5005/report?domain=${domain}`).catch(() => { });
                location.replace(chrome.runtime.getURL("blocked.html"));
            }
        } catch (error) {
            // If the C# app is closed, this will trigger.
            // We do nothing and let the page stay as-is.
            clearTimeout(timeoutId);
        }
    };

    const intervalId = setInterval(checkOnce, 2000);
    checkOnce();
})();
