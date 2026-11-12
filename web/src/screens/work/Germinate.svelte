<script lang="ts">
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import Stepper from "./Stepper.svelte";

  // Germinated, on a tray nobody has counted: a rough count now is welcome, but never needed.
  let {
    container,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  const line = $derived(
    container.contents.find((item) => item.notCounted) ?? container.contents[0],
  );
  const name = $derived(plantName(line.commonName, line.variety));

  let up = $state(50);
  let problem = $state("");
  let busy = $state(false);

  async function record(withCount: boolean) {
    busy = true;
    problem = "";

    // Both wait on the device if there's no signal, and go in this order.
    const germinated = await recordWork(work.germinated(container.id));

    if (!isRecorded(germinated)) {
      busy = false;
      problem = outcomeProblem(germinated);
      return;
    }

    if (withCount) {
      const counted = await recordWork(
        work.count(container.id, { batchId: line.batchId, counted: up }),
      );

      if (!isRecorded(counted)) {
        busy = false;
        problem = `Germinated is saved, but the count wasn't: ${outcomeProblem(counted)}`;
        return;
      }
    }

    busy = false;
    onDone(
      withCount
        ? `${container.labelCode} germinated: about ${up} ${name} up`
        : `${container.labelCode} germinated`,
      germinated.eventId,
    );
  }
</script>

<ActionFrame title="Germinated" {problem} {onCancel} {container}>
  <p>
    Roughly how many are up? A guess is fine: you'll count properly when you
    prick them out.
  </p>
  <Stepper bind:value={up} presets={[20, 50, 100, 200]} />
  <button disabled={busy} onclick={() => record(true)}>Save</button>
  <button class="secondary" disabled={busy} onclick={() => record(false)}
    >Not sure yet</button
  >
</ActionFrame>
