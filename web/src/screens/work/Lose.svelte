<script lang="ts">
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import {
    lossReasons,
    isRecorded,
    outcomeProblem,
    plantName,
    work,
    type WorkRequest,
  } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
  import { quantityPresets } from "../../lib/tray";
  import Stepper from "./Stepper.svelte";

  let {
    container,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  let chosen = $state<string | null>(null);
  let quantity = $state(1);
  // For a tray nobody has counted: all of them, or some.
  let all = $state<boolean | null>(null);
  let problem = $state("");
  let busy = $state(false);

  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  async function record(reason: Schemas["LossReason"], label: string) {
    busy = true;
    const item: WorkRequest = work.lose(container.id, {
      batchId: line.batchId,
      quantity: line.notCounted ? 0 : quantity,
      reason,
      all: line.notCounted ? all : null,
    });
    const outcome = await recordWork(item);
    busy = false;

    if (isRecorded(outcome)) {
      onDone(
        `Lost ${line.notCounted ? (all ? "all the" : "some") : quantity} ${plantName(line.commonName, line.variety)} (${label})`,
        outcome.eventId,
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Lost" {problem} {onCancel} {container}>
  <div class="action-columns">
    <div class="stack">
      <BatchChoice lines={container.contents} bind:selected={chosen} />
      {#if line.notCounted}
        <p>Nobody has counted these yet. Were they all lost, or some?</p>
        <div class="segmented two">
          <button aria-pressed={all === true} onclick={() => (all = true)}
            >All of them</button
          >
          <button aria-pressed={all === false} onclick={() => (all = false)}
            >Some of them</button
          >
        </div>
        {#if all === null}
          <p class="muted">Choose all or some, then why.</p>
        {/if}
      {:else}
        <p>How many?</p>
        <Stepper
          bind:value={quantity}
          max={Number(line.quantity)}
          presets={quantityPresets(Number(line.quantity))}
        />
      {/if}
    </div>
    <div class="stack">
      <p>Why?</p>
      {#each lossReasons as option (option.reason)}
        <button
          disabled={busy || (line.notCounted && all === null)}
          onclick={() => record(option.reason, option.label)}
          >{option.label}</button
        >
      {/each}
    </div>
  </div>
</ActionFrame>
