import createClient from "openapi-fetch";
import type { paths } from "./schema";

export const api = createClient<paths>({ baseUrl: "" });

export const sessionEnded = "growlog:session-ended";

// A session can end at any time, such as when a bench station is left idle.
api.use({
  onResponse({ response }) {
    if (response.status === 401) {
      window.dispatchEvent(new Event(sessionEnded));
    }
  },
});
