<script lang="ts">
  import { api } from "../api/client";
  import type { Invite } from "../lib/boot";
  import { createPasskey, passkeyProblem } from "../lib/passkey";

  let { invite, onDone }: { invite: Invite; onDone: () => void } = $props();

  let problem = $state("");
  let busy = $state(false);

  async function setUp() {
    busy = true;
    problem = "";

    try {
      const options = await api.POST("/api/invites/passkey-options", {
        body: invite,
        parseAs: "text",
      });

      if (!options.response.ok) {
        problem =
          "This invitation has expired or been used. Ask the owner for a new one.";
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
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = false;
    }
  }
</script>

<main>
  <img class="hero-logo" src="/logo-96.png" alt="" />
  <h1>Welcome to GrowLog</h1>
  <p>Your phone will save a passkey, so you can sign in without a password.</p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button onclick={setUp} disabled={busy}>Set up my passkey</button>
</main>
