<script lang="ts">
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
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
  let problem = $state("");
  let busy = $state(false);

  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  let counted = $derived(Number(line.quantity));

  async function record() {
    busy = true;
    const outcome = await recordWork(
      work.count(container.id, { batchId: line.batchId, counted }),
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
  <p>How many are there?</p>
  <Stepper bind:value={counted} min={0} />
  <button disabled={busy} onclick={record}>Done</button>
</ActionFrame>
