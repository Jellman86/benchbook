<script lang="ts">
  import { api } from "../api/client";
  import type { Person } from "../lib/boot";

  let { people, onSignedIn }: { people: Person[]; onSignedIn: () => void } =
    $props();

  const maxPinLength = 6;
  const minPinLength = 4;

  let person = $state<Person | null>(null);
  let pin = $state("");
  let problem = $state("");
  let busy = $state(false);

  function choose(chosen: Person) {
    person = chosen;
    pin = "";
    problem = "";
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

    busy = true;
    problem = "";

    try {
      const { response } = await api.POST("/api/bench/sign-in", {
        body: { userId: person.id, pin },
      });

      if (response.ok) {
        onSignedIn();
      } else if (response.status === 423) {
        problem =
          "Too many wrong tries. Wait 15 minutes, or ask the owner to set a new PIN.";
      } else {
        problem = "That PIN isn't right. Please try again.";
      }
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = false;
      pin = "";
    }
  }
</script>

{#if person}
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
    {#if people.length === 0}
      <p class="muted">
        No staff have been added yet. Ask the owner to add you.
      </p>
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
