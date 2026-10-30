// ELEVEN's service worker. BuildScript writes it over the one from Unity's PWA template, filling
// in the build id and the player's files.
//
// The template's worker answers from any cache first, never deletes old caches and works with
// file names that are the same in every build, so a device keeps running the first build it saw.
// This one goes to the network first, revalidating so a new deploy is seen at once (unchanged
// files cost a 304), and falls back to its cache only when the network fails. Each build has its
// own cache; once it takes over it deletes older ones for the same link.

const build = "24+59205d0";
const files = ["Build/WebGL.data.unityweb", "Build/WebGL.framework.js.unityweb", "Build/WebGL.loader.js", "Build/WebGL.wasm.unityweb", "TemplateData/favicon.ico", "TemplateData/icons/unity-logo-dark.png", "TemplateData/icons/unity-logo-light.png", "TemplateData/progress-bar-empty-dark.png", "TemplateData/progress-bar-empty-light.png", "TemplateData/progress-bar-full-dark.png", "TemplateData/progress-bar-full-light.png", "TemplateData/style.css", "TemplateData/unity-logo-dark.png", "TemplateData/unity-logo-light.png", "TemplateData/webmemd-icon.png", "icons/eleven-180.png", "icons/eleven-192.png", "icons/eleven-32.png", "icons/eleven-512.png", "icons/eleven.svg", "index.html", "manifest.webmanifest"];

const scope = self.registration.scope;
const prefix = `eleven ${scope} `;
const cacheName = prefix + build;

// The PWA template's caches, named "<company>-<product>-<version>". Cache names are shared by the
// whole origin, so one belongs to this link only if every file in it does.
const templatePrefix = "Enkel Design-ELEVEN-";

// Previews under pr/<number>/ have workers of their own.
const inScope = (url) => url.startsWith(scope) && !url.startsWith(`${scope}pr/`);

async function isTemplateCacheOfThisLink(name) {
  if (!name.startsWith(templatePrefix)) return false;
  const requests = await (await caches.open(name)).keys();
  return requests.length > 0 && requests.every((request) => inScope(request.url));
}

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
      if (name !== cacheName && (name.startsWith(prefix) || await isTemplateCacheOfThisLink(name))) {
        await caches.delete(name);
      }
    }
    await self.clients.claim();
  })());
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET" || !inScope(request.url)) return;

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
