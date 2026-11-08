<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import { outcomeProblem, plantName, submit, work } from "../../lib/work";
  import Count from "./Count.svelte";
  import Lose from "./Lose.svelte";
  import Move from "./Move.svelte";
  import Note from "./Note.svelte";
  import Transfer from "./Transfer.svelte";
  import Treat from "./Treat.svelte";

  let {
    label,
    onDone,
    onCancel,
    onNotFound,
  }: {
    label: string;
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
    onNotFound: (message: string) => void;
  } = $props();

  type Action =
    "lost" | "move" | "potOn" | "prickOut" | "count" | "treat" | "note";

  let container = $state<Schemas["ContainerResponse"] | null>(null);
  let action = $state<Action | null>(null);
  let problem = $state("");
  let busy = $state(false);

  const hasPlants = $derived((container?.contents.length ?? 0) > 0);
  const back = () => (action = null);

  onMount(async () => {
    try {
      const { data } = await api.GET("/api/containers/by-label/{label}", {
        params: { path: { label } },
      });

      if (data) {
        container = data;
      } else {
        onNotFound(`No container has the label ${label}.`);
      }
    } catch {
      onNotFound("Can't reach GrowLog. Check the connection and try again.");
    }
  });

  async function germinated() {
    if (!container) {
      return;
    }

    busy = true;
    const outcome = await submit(work.germinated(container.id));
    busy = false;

    if (outcome.kind === "stored") {
      onDone(`${container.labelCode} germinated`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

{#if !container}
  <main><p class="muted">Loading…</p></main>
{:else if action === "lost"}
  <Lose {container} {onDone} onCancel={back} />
{:else if action === "move"}
  <Move {container} {onDone} onCancel={back} />
{:else if action === "potOn"}
  <Transfer {container} type="PottedOn" {onDone} onCancel={back} />
{:else if action === "prickOut"}
  <Transfer {container} type="PrickedOut" {onDone} onCancel={back} />
{:else if action === "count"}
  <Count {container} {onDone} onCancel={back} />
{:else if action === "treat"}
  <Treat {container} {onDone} onCancel={back} />
{:else if action === "note"}
  <Note {container} {onDone} onCancel={back} />
{:else}
  <main>
    <h1>{container.labelCode}</h1>
    {#each container.contents as line (line.batchId)}
      <p>
        <strong>{line.quantity}</strong>
        {plantName(line.commonName, line.variety)}
        <span class="muted">{line.traceabilityCode}</span>
      </p>
    {:else}
      <p class="muted">This container is empty.</p>
    {/each}
    <p class="muted">
      {container.locationName ?? "No place recorded"}{container.potSizeName
        ? `, ${container.potSizeName}`
        : ""}
    </p>

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <div class="stack">
      {#if hasPlants}
        <button onclick={() => (action = "lost")}>Lost</button>
        <button onclick={() => (action = "potOn")}>Pot on</button>
        <button onclick={() => (action = "prickOut")}>Prick out</button>
        <button disabled={busy} onclick={germinated}>Germinated</button>
        <button onclick={() => (action = "count")}>Count</button>
      {/if}
      <button onclick={() => (action = "move")}>Move</button>
      <button onclick={() => (action = "treat")}>Treated</button>
      <button onclick={() => (action = "note")}>Note</button>
    </div>
    <button class="secondary" onclick={onCancel}>Back</button>
  </main>
{/if}
