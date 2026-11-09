<script lang="ts">
  import { api } from "../api/client";
  import { passkeyProblem, usePasskey } from "../lib/passkey";

  let { onSignedIn }: { onSignedIn: () => void } = $props();

  let problem = $state("");
  let busy = $state(false);

  async function signIn() {
    busy = true;
    problem = "";

    try {
      const options = await api.POST("/api/account/passkey-options", {
        parseAs: "text",
      });
      let credential;

      try {
        credential = await usePasskey(options.data as unknown as string);
      } catch (error) {
        problem = passkeyProblem(error);
        return;
      }

      const { response } = await api.POST("/api/account/sign-in", {
        body: { credential: credential as never },
      });

      if (response.ok) {
        onSignedIn();
      } else {
        problem = "That passkey wasn't recognised.";
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
  <h1>GrowLog</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button onclick={signIn} disabled={busy}>Sign in with passkey</button>
</main>
