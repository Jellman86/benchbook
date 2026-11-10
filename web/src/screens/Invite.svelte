<script lang="ts">
  import { api } from "../api/client";
  import type { Invite } from "../lib/boot";
  import { createPasskey, passkeyProblem } from "../lib/passkey";

  let { invite, onDone }: { invite: Invite; onDone: () => void } = $props();

  const expired =
    "This invitation has expired or been used. Ask the owner for a new one.";

  let password = $state("");
  let problem = $state("");
  let busy = $state(false);

  async function during(work: () => Promise<void>) {
    busy = true;
    problem = "";

    try {
      await work();
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = false;
    }
  }

  const usePassword = (event: SubmitEvent) => {
    event.preventDefault();

    return during(async () => {
      const { response } = await api.POST(
        "/api/invites/complete-with-password",
        { body: { ...invite, password } },
      );

      if (response.ok) {
        onDone();
      } else {
        problem =
          response.status === 403
            ? expired
            : "A password must be at least 8 characters.";
      }
    });
  };

  const setUpPasskey = () =>
    during(async () => {
      const options = await api.POST("/api/invites/passkey-options", {
        body: invite,
        parseAs: "text",
      });

      if (!options.response.ok) {
        problem = expired;
        return;
      }

      let credential;

      try {
        credential = await createPasskey(options.data as unknown as string);
      } catch (error) {
        problem = passkeyProblem(error);
        return;
      }

      const { response } = await api.POST("/api/invites/complete", {
        body: { ...invite, credential: credential as never },
      });

      if (response.ok) {
        onDone();
      } else {
        problem = "That didn't finish. Please try again.";
      }
    });
</script>

<main>
  <img class="hero-logo" src="/icon-192.png" alt="" />
  <h1>Welcome to GrowLog</h1>
  <p>Save a passkey on your phone, or choose a password.</p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button onclick={setUpPasskey} disabled={busy}>Set up my passkey</button>

  <form class="stack" onsubmit={usePassword}>
    <label>
      Password (at least 8 characters)
      <input
        type="password"
        bind:value={password}
        autocomplete="new-password"
        required
      />
    </label>
    <button type="submit" class="secondary" disabled={busy}
      >Use this password</button
    >
  </form>
</main>
