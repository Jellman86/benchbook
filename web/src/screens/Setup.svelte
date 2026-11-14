<script lang="ts">
  import { api } from "../api/client";
  import { createPasskey, passkeyProblem } from "../lib/passkey";

  let { onDone }: { onDone: () => void } = $props();

  let token = $state("");
  let name = $state("");
  let password = $state("");
  let problem = $state("");
  let busy = $state(false);

  async function during(work: () => Promise<void>) {
    busy = true;
    problem = "";

    try {
      await work();
    } catch {
      problem = "Can't reach Hortiplanner. Check the connection and try again.";
    } finally {
      busy = false;
    }
  }

  const useAPassword = (event: SubmitEvent) => {
    event.preventDefault();

    return during(async () => {
      const { response } = await api.POST("/api/setup/complete-with-password", {
        body: { token, name, password },
      });

      if (response.ok) {
        onDone();
      } else {
        problem =
          response.status === 403
            ? "That setup code isn't right."
            : "Please enter your name and a password of at least 8 characters.";
      }
    });
  };

  const useAPasskey = () =>
    during(async () => {
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
    });
</script>

<main>
  <img class="hero-logo" src="/icon-192.png" alt="" />
  <h1>Set up Hortiplanner</h1>
  <p>
    You'll be the owner. Choose a password, or save a passkey on this phone or
    computer.
  </p>

  <form class="stack" onsubmit={useAPassword}>
    <label>
      Setup code
      <input bind:value={token} autocomplete="off" required />
    </label>
    <label>
      Your name
      <input bind:value={name} autocomplete="name" required />
    </label>
    <label>
      Password (at least 8 characters)
      <input
        type="password"
        bind:value={password}
        autocomplete="new-password"
      />
    </label>

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <button type="submit" disabled={busy}>Use this password</button>
    <button
      type="button"
      class="secondary"
      disabled={busy}
      onclick={useAPasskey}>Create my passkey</button
    >
  </form>
</main>
