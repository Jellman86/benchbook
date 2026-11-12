<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";

  let {
    container,
    when,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    /** When the work was done, if not just now. */
    when?: string;
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  let product = $state("");
  let dose = $state("");
  let area = $state("");
  let problem = $state("");
  let busy = $state(false);
  let choices = $state<Schemas["TreatmentChoices"] | null>(null);

  // What's been used before can be chosen rather than typed with wet hands.
  onMount(async () => {
    try {
      choices = (await api.GET("/api/treatments/choices")).data ?? null;
    } catch {
      choices = null;
    }
  });

  async function record(event: SubmitEvent) {
    event.preventDefault();
    busy = true;
    const outcome = await recordWork(
      work.treat(container.id, { product, dose, area }, when),
    );
    busy = false;

    if (isRecorded(outcome)) {
      onDone(`Treated ${container.labelCode} with ${product}`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Treated" {problem} {onCancel} {container}>
  <form class="stack" onsubmit={record}>
    <label>Product <input bind:value={product} required /></label>
    {#if choices?.products.length}
      <div class="choice-row" aria-label="Products used before">
        {#each choices.products as choice (choice)}
          <button
            type="button"
            class="secondary"
            aria-pressed={product === choice}
            onclick={() => (product = choice)}>{choice}</button
          >
        {/each}
      </div>
    {/if}
    <label>Dose <input bind:value={dose} required /></label>
    {#if choices?.doses.length}
      <div class="choice-row" aria-label="Doses used before">
        {#each choices.doses as choice (choice)}
          <button
            type="button"
            class="secondary"
            aria-pressed={dose === choice}
            onclick={() => (dose = choice)}>{choice}</button
          >
        {/each}
      </div>
    {/if}
    <label>Area or crop treated <input bind:value={area} required /></label>
    {#if choices?.areas.length}
      <div class="choice-row" aria-label="Areas used before">
        {#each choices.areas as choice (choice)}
          <button
            type="button"
            class="secondary"
            aria-pressed={area === choice}
            onclick={() => (area = choice)}>{choice}</button
          >
        {/each}
      </div>
    {/if}
    <button type="submit" disabled={busy}>Done</button>
  </form>
</ActionFrame>
