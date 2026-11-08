<script lang="ts">
  import { api } from "../api/client";
  import { createPasskey, passkeyProblem } from "../lib/passkey";

  let { onDone }: { onDone: () => void } = $props();

  let token = $state("");
  let name = $state("");
  let problem = $state("");
  let busy = $state(false);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    busy = true;
    problem = "";

    try {
      const options = await api.POST("/api/setup/passkey-options", {
        body: { token, name },
        parseAs: "text",
      });

      if (!options.response.ok) {
        problem =
          options.response.status === 403
            ? "That setup code isn't right."
            : "Please enter your name.";
        return;
      }

      let credential;

      try {
        credential = await createPasskey(options.data as unknown as string);
      } catch (error) {
        problem = passkeyProblem(error);
        return;
      }

      const { response } = await api.POST("/api/setup/complete", {
        body: { token, credential: credential as never },
      });

      if (response.ok) {
        onDone();
      } else {
        problem = "Setup didn't finish. Please try again.";
      }
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = false;
    }
  }
</script>

<main>
  <h1>Set up GrowLog</h1>
  <p>
    You'll be the owner. Your phone or computer will save a passkey, so there's
    no password to remember.
  </p>

  <form class="stack" onsubmit={submit}>
    <label>
      Setup code
      <input bind:value={token} autocomplete="off" required />
    </label>
    <label>
      Your name
      <input bind:value={name} autocomplete="name" required />
    </label>

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <button type="submit" disabled={busy}>Create my passkey</button>
  </form>
</main>
