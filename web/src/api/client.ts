import createClient from "openapi-fetch";
import type { paths } from "./schema";

export const api = createClient<paths>({ baseUrl: "" });

export const sessionEnded = "hortiplanner:session-ended";

// Asking who is signed in is expected to answer 401, so it isn't a session ending.
const probes = ["/api/account/me", "/api/bench/people"];

// A session can end at any time, such as when a bench station is left idle.
api.use({
  onResponse({ request, response }) {
    if (
      response.status === 401 &&
      !probes.includes(new URL(request.url).pathname)
    ) {
      window.dispatchEvent(new Event(sessionEnded));
    }
  },
});
