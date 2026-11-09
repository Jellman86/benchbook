// Lets the app open with no signal. Recording is handled by the work queue, so the API is never cached here.
const cacheName = "growlog-v2";

async function precache() {
  const cache = await caches.open(cacheName);
  const response = await fetch("/index.html", { cache: "no-store" });
  const html = await response.clone().text();
  await cache.put("/index.html", response);

  const assets = [...html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)];
  await cache.addAll(assets.map((match) => match[1]));
}

async function openPage(request) {
  try {
    const response = await fetch(request);

    if (response.ok) {
      const cache = await caches.open(cacheName);
      await cache.put("/index.html", response.clone());
    }

    return response;
  } catch {
    return caches.match("/index.html");
  }
}

async function fromCacheFirst(request) {
  const cached = await caches.match(request);

  if (cached) {
    return cached;
  }

  const response = await fetch(request);

  if (response.ok) {
    const cache = await caches.open(cacheName);
    await cache.put(request, response.clone());
  }

  return response;
}

self.addEventListener("install", (event) => {
  event.waitUntil(precache().then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  if (
    event.request.method !== "GET" ||
    url.origin !== self.location.origin ||
    url.pathname.startsWith("/api/") ||
    url.pathname === "/signin-oidc"
  ) {
    return;
  }

  event.respondWith(
    event.request.mode === "navigate"
      ? openPage(event.request)
      : fromCacheFirst(event.request),
  );
});
