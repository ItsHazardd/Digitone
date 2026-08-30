if (location.origin === 'https://covers.musichoarders.xyz') {
    localStorage.setItem('theme', 'dark');
    try {
        let tours = JSON.parse(localStorage.getItem('tours') || '[]');
        if (!Array.isArray(tours)) tours = [];
        ['search', 'results', 'integration'].forEach(t => {
            if (!tours.includes(t)) tours.push(t);
        });
        localStorage.setItem('tours', JSON.stringify(tours));
    } catch (ignore) {}

    window.addEventListener('message', e => {
        if (e.source !== window || e.origin !== location.origin) return;
        try {
            const message = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
            if (message && message.type === 'pick') window.chrome.webview.postMessage(message);
        } catch (ignore) {}
    });

    function addWebsiteLink() {
        const bar = document.querySelector('.integration .wrapper');
        if (!bar || document.getElementById('digitone-cov-link')) return;
        const link = document.createElement('a');
        link.id = 'digitone-cov-link';
        link.href = 'https://covers.musichoarders.xyz/';
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        link.textContent = 'Open COV ↗';
        link.title = 'Open covers.musichoarders.xyz in your browser';
        link.style.cssText = 'color:inherit;margin-left:16px;text-decoration:underline;font-weight:600;white-space:nowrap';
        bar.appendChild(link);
    }
    window.addEventListener('DOMContentLoaded', () => {
        addWebsiteLink();
        new MutationObserver(addWebsiteLink).observe(document.body, {childList:true, subtree:true});
    });
}
