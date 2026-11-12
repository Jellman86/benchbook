<script lang="ts">
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
  import Stepper from "./Stepper.svelte";

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

  let chosen = $state<string | null>(null);
  let problem = $state("");
  let busy = $state(false);

  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  // Nobody has counted an uncounted tray, so there's no number to start from.
  let counted = $derived(line.notCounted ? 0 : Number(line.quantity));

  async function record() {
    busy = true;
    const outcome = await recordWork(
      work.count(container.id, { batchId: line.batchId, counted }, when),
    );
    busy = false;

    if (isRecorded(outcome)) {
      onDone(
        `Counted ${counted} ${plantName(line.commonName, line.variety)}`,
        outcome.eventId,
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Count" {problem} {onCancel} {container}>
  <BatchChoice lines={container.contents} bind:selected={chosen} />
  {#if line.notCounted}
    <p>Nobody has counted these yet. How many are there?</p>
  {:else}
    <p>How many are there?</p>
  {/if}
  <Stepper bind:value={counted} min={0} />
  {#if line.notCounted && counted < 1}
    <p class="muted">
      Enter how many. If none came up, use Lost and "All of them" instead.
    </p>
  {/if}
  <button disabled={busy || (line.notCounted && counted < 1)} onclick={record}
    >Done</button
  >
</ActionFrame>
