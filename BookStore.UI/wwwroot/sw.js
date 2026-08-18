// نون گرد — app-shell service worker.
// Conservative strategy: never serve stale app code, never cache private data.
//  - /api/* and /uploads/books/* (gated book files) : network only, never cached
//  - _framework/* (WASM boot + versioned app assemblies) : network first (always fresh)
//  - navigation : network first, fall back to the cached shell (offline first load)
//  - other same-origin static assets (css/js/fonts/images incl. covers) : cache-first
//    with a background refresh, so a second visit picks up updates
// Covers are safe to cache because re-uploads get a fresh unique filename.
// Note: full offline boot of a Blazor WASM app would require precaching the entire
// _framework payload; we intentionally don't, so the app still needs the network once.

const CACHE = 'nun-gerd-v1';
const SHELL = [
    '/',
    '/index.html',
    '/css/app.css',
    '/BookStore.UI.styles.css',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap-icons/bootstrap-icons.min.css',
    '/lib/bootstrap-icons/fonts/bootstrap-icons.woff2',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
    '/fonts/Peyda/01-Standard/WebFonts/fonts/woff2/PeydaWeb-Regular.woff2',
    '/icon-192.png',
    '/favicon.png'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE)
            .then((cache) => cache.addAll(SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', (event) => {
    const request = event.request;

    if (request.method !== 'GET') {
        return;
    }

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return;
    }

    // Private or always-fresh resources: pass through untouched (auth headers are preserved
    // because we forward the original Request object).
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/uploads/books/')) {
        return;
    }

    // App code must never be stale.
    if (url.pathname.includes('/_framework/')) {
        event.respondWith(fetch(request));
        return;
    }

    // Offline fallback for the app shell.
    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request).catch(() => caches.match('/index.html'))
        );
        return;
    }

    // Static assets: cache-first, refresh in the background.
    event.respondWith(
        caches.match(request).then((cached) => {
            const network = fetch(request).then((response) => {
                if (response.ok) {
                    const clone = response.clone();
                    caches.open(CACHE).then((cache) => cache.put(request, clone));
                }
                return response;
            }).catch(() => cached);
            return cached || network;
        })
    );
});
