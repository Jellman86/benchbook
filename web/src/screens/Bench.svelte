<script lang="ts">
  import { api } from "../api/client";
  import { onMount } from "svelte";
  import type { Person } from "../lib/boot";

  let { people, onSignedIn }: { people: Person[]; onSignedIn: () => void } =
    $props();

  const maxPinLength = 6;
  const minPinLength = 4;

  let person = $state<Person | null>(null);
  let pin = $state("");
  let problem = $state("");
  let busy = $state(false);
  let needsPin = $state(true);

  // Some nurseries choose tapping a name at the bench, with no PIN.
  onMount(async () => {
    try {
      needsPin =
        (await api.GET("/api/account/sign-in-mode")).data?.benchNeedsPin ??
        true;
    } catch {
      needsPin = true;
    }
  });

  // Lets the owner sign in on this device again, rather than being stuck on an empty list.
  async function leave() {
    await api.POST("/api/bench/leave");
    location.reload();
  }

  function choose(chosen: Person) {
    person = chosen;
    pin = "";
    problem = "";

    if (!needsPin) {
      void submit();
    }
  }

  function press(digit: number) {
    if (pin.length < maxPinLength) {
      pin += digit;
    }
  }

  async function submit() {
    if (!person) {
      return;
    }

    // Taken now, so digits pressed while it's checked start the next try rather than being wiped.
    const entered = pin;
    pin = "";
    busy = true;
    problem = "";

    try {
      const { response } = await api.POST("/api/bench/sign-in", {
        body: { userId: person.id, pin: entered },
      });

      if (response.ok) {
        onSignedIn();
      } else if (response.status === 423) {
        problem =
          "Too many wrong tries. Wait 15 minutes, or ask the owner to set a new PIN.";
      } else {
        problem = needsPin
          ? "That PIN isn't right. Please try again."
          : "That didn't work. Please try again.";
      }
    } catch {
      problem = "Can't reach Hortiplanner. Check the connection and try again.";
    } finally {
      busy = false;
    }
  }
</script>

{#if person && needsPin}
  <main>
    <h1>{person.name}</h1>
    <p class="pin-display" aria-label="PIN digits entered">
      {"•".repeat(pin.length)}
    </p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <div class="pin-pad">
      {#each [1, 2, 3, 4, 5, 6, 7, 8, 9] as digit (digit)}
        <button class="secondary" onclick={() => press(digit)}>{digit}</button>
      {/each}
      <button
        class="secondary"
        onclick={() => (pin = pin.slice(0, -1))}
        aria-label="Delete last digit">⌫</button
      >
      <button class="secondary" onclick={() => press(0)}>0</button>
      <button
        onclick={submit}
        disabled={busy || pin.length < minPinLength}
        aria-label="Sign in">OK</button
      >
    </div>

    <button class="secondary" onclick={() => (person = null)}
      >That's not me</button
    >
  </main>
{:else}
  <main>
    <h1>Who's working?</h1>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if people.length === 0}
      <p class="muted">
        Nobody has a bench PIN yet. The owner can give people one under Staff.
      </p>
      <button class="secondary" onclick={leave}
        >Stop being a bench station</button
      >
    {/if}
    <div class="stack">
      {#each people as entry (entry.id)}
        <button class="secondary" onclick={() => choose(entry)}
          >{entry.name}</button
        >
      {/each}
    </div>
  </main>
{/if}
