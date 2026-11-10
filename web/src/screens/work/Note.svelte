<script lang="ts">
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";

  let {
    container,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  let note = $state("");
  let problem = $state("");
  let busy = $state(false);

  async function record(event: SubmitEvent) {
    event.preventDefault();
    busy = true;
    const outcome = await recordWork(work.note(container.id, { note }));
    busy = false;

    if (isRecorded(outcome)) {
      onDone(`Note added to ${container.labelCode}`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Note" {problem} {onCancel} {container}>
  <form class="stack" onsubmit={record}>
    <label>Note <input bind:value={note} required /></label>
    <p class="muted">
      Notes are kept permanently. Please don't include anyone's name or contact
      details.
    </p>
    <button type="submit" disabled={busy}>Done</button>
  </form>
</ActionFrame>
