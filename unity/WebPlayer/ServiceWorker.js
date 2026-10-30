// ELEVEN's service worker. BuildScript writes it over the one from Unity's PWA template, filling
// in the build id and the player's files.
//
// The template's worker answers from any cache first, never deletes old caches and works with
// file names that are the same in every build, so a device keeps running the first build it saw.
// This one goes to the network first, revalidating so a new deploy is seen at once (unchanged
// files cost a 304), and falls back to its cache only when the network fails. Each build has its
// own cache; once it takes over it deletes older ones for the same scope.

const build = "%BUILD_ID%";
const files = [/* FILES */];

const scope = self.registration.scope;
const prefix = `eleven ${scope} `;
const cacheName = prefix + build;

// The PWA template's caches, named "<company>-<product>-<version>".
const templatePrefix = "Enkel Design-ELEVEN-";

self.addEventListener("install", (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(cacheName);
    await cache.addAll(["./", ...files].map((file) => new Request(file, { cache: "no-cache" })));
    await self.skipWaiting();
  })());
});

self.addEventListener("activate", (event) => {
  event.waitUntil((async () => {
    for (const name of await caches.keys()) {
      if (name !== cacheName && (name.startsWith(prefix) || name.startsWith(templatePrefix))) {
        await caches.delete(name);
      }
    }
    await self.clients.claim();
  })());
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  // Previews under pr/<number>/ have workers of their own.
  if (request.method !== "GET" || !request.url.startsWith(scope) || request.url.startsWith(`${scope}pr/`)) return;

  event.respondWith((async () => {
    const cache = await caches.open(cacheName);
    try {
      const response = await fetch(request.url, { cache: "no-cache" });
      if (response.ok) event.waitUntil(cache.put(request, response.clone()));
      return response;
    } catch (error) {
      // Offline. Navigations may carry options such as ?trace, which the cached page ignores.
      const cached = await cache.match(request, { ignoreSearch: request.mode === "navigate" });
      if (cached) return cached;
      throw error;
    }
  })());
});
