<script lang="ts">
  import type { Schemas } from "../../lib/types";
  import { outcomeProblem, submit, work } from "../../lib/work";
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

  let product = $state("");
  let dose = $state("");
  let area = $state("");
  let problem = $state("");
  let busy = $state(false);

  async function record(event: SubmitEvent) {
    event.preventDefault();
    busy = true;
    const outcome = await submit(
      work.treat(container.id, { product, dose, area }),
    );
    busy = false;

    if (outcome.kind === "stored") {
      onDone(`Treated ${container.labelCode} with ${product}`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Treated" {problem} {onCancel}>
  <form class="stack" onsubmit={record}>
    <label>Product <input bind:value={product} required /></label>
    <label>Dose <input bind:value={dose} required /></label>
    <label>Area or crop treated <input bind:value={area} required /></label>
    <button type="submit" disabled={busy}>Done</button>
  </form>
</ActionFrame>
