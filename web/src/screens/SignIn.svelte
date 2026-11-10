<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../api/client";
  import { externalSignInProblem, signInAsYourselfKey } from "../lib/boot";
  import { passkeyProblem, usePasskey } from "../lib/passkey";
  import type { Schemas } from "../lib/types";

  type Person = Schemas["SignInPerson"];

  let { onSignedIn }: { onSignedIn: () => void } = $props();

  const offline = "Can't reach GrowLog. Check the connection and try again.";

  let people = $state<Person[]>([]);
  let external = $state<Schemas["ExternalSignInOptions"] | null>(null);
  let chosen = $state<Person | null>(null);
  let password = $state("");
  let problem = $state("");
  let busy = $state(false);
  let open = $state(false);

  onMount(async () => {
    try {
      problem = externalSignInProblem(location.search);
      history.replaceState(null, "", location.pathname);
      people = (await api.GET("/api/account/people")).data ?? [];
      external = (await api.GET("/api/account/external")).data ?? null;
      open =
        (await api.GET("/api/account/sign-in-mode")).data?.level === "Open";
    } catch {
      problem = offline;
    }
  });

  async function during(work: () => Promise<void>) {
    busy = true;
    problem = "";

    try {
      await work();
    } catch {
      problem = offline;
    } finally {
      busy = false;
    }
  }

  // The nursery chose no signing in: go back in as the nursery team.
  function carryOn() {
    try {
      sessionStorage.removeItem(signInAsYourselfKey);
    } catch {
      // Nothing was kept, so there's nothing to clear.
    }

    onSignedIn();
  }

  const signInWithPasskey = () =>
    during(async () => {
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
    });

  const signInByName = (person: Person) =>
    during(async () => {
      const { response } = await api.POST("/api/account/sign-in/name", {
        body: { userId: person.id },
      });

      if (response.ok) {
        onSignedIn();
      } else {
        problem = "That didn't work. Ask the owner to check your account.";
      }
    });

  function choose(person: Person) {
    problem = "";
    password = "";

    if (person.nameOnly) {
      void signInByName(person);
    } else if (person.hasPassword) {
      chosen = person;
    } else {
      void signInWithPasskey();
    }
  }

  const signInWithPassword = (event: SubmitEvent, person: Person) => {
    event.preventDefault();

    return during(async () => {
      const { response } = await api.POST("/api/account/sign-in/password", {
        body: { userId: person.id, password },
      });

      if (response.ok) {
        onSignedIn();
      } else {
        problem =
          response.status === 423
            ? "Too many wrong tries. Wait 15 minutes, or ask the owner for a new password."
            : "That password isn't right.";
      }
    });
  };
</script>

<main>
  <img class="hero-logo" src="/logo-96.png" alt="" />
  <h1>GrowLog</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if chosen}
    {@const person = chosen}
    <h2>{person.name}</h2>
    <form class="stack" onsubmit={(event) => signInWithPassword(event, person)}>
      <!-- Lets a password manager tell people apart on a shared device. -->
      <input hidden autocomplete="username" value={person.name} readonly />
      <label>
        Password
        <input
          type="password"
          bind:value={password}
          autocomplete="current-password"
          required
        />
      </label>
      <button type="submit" disabled={busy}>Sign in</button>
    </form>
    <button class="back" onclick={() => (chosen = null)}>Back</button>
  {:else}
    {#if external?.enabled}
      <a class="button" href="/api/account/external/login"
        >Sign in with {external.name}</a
      >
    {/if}
    {#if people.length > 0}
      <h2>Who's signing in?</h2>
      <div class="card-grid">
        {#each people as person (person.id)}
          <button
            class="secondary"
            disabled={busy}
            onclick={() => choose(person)}>{person.name}</button
          >
        {/each}
      </div>
    {/if}
    <button
      class={people.length > 0 ? "secondary" : ""}
      onclick={signInWithPasskey}
      disabled={busy}>Sign in with passkey</button
    >
    {#if open}
      <button class="secondary" onclick={carryOn}
        >Carry on without signing in</button
      >
    {/if}
  {/if}
</main>
