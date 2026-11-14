<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import Stepper from "./Stepper.svelte";

  // Germinated, on a tray nobody has counted: a count or best estimate now is welcome, but never needed.
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

  const line = $derived(
    container.contents.find((item) => item.notCounted) ?? container.contents[0],
  );
  const name = $derived(plantName(line.commonName, line.variety));

  // Starts empty: whatever is entered becomes the number in the tray.
  let up = $state(0);
  // Most people guess as seedlings come up; a guess is kept as one, so pricking out still counts them.
  let rough = $state(true);
  let problem = $state("");
  let busy = $state(false);
  // Seed sown by weight: what earlier sowings of this plant gave per gram, offered to tap rather than filled in.
  let guess = $state<Schemas["SeedlingGuess"] | null>(null);

  onMount(async () => {
    try {
      guess =
        (
          await api.GET("/api/batches/{id}/seedling-guess", {
            params: { path: { id: line.batchId } },
          })
        ).data ?? null;
    } catch {
      // No signal: no guess, and nothing else is lost.
    }
  });

  async function record(withCount: boolean) {
    busy = true;
    problem = "";

    // One request keeps germination and its optional count together, including Undo and offline capture.
    const germinated = await recordWork(
      work.germinated(
        container.id,
        when,
        withCount
          ? {
              countEventId: crypto.randomUUID(),
              batchId: line.batchId,
              counted: up,
              rough,
            }
          : {},
      ),
    );
    if (!isRecorded(germinated)) {
      busy = false;
      problem = outcomeProblem(germinated);
      return;
    }

    busy = false;
    onDone(
      withCount
        ? `${container.labelCode} germinated: ${rough ? "about " : ""}${up} ${name} up`
        : `${container.labelCode} germinated`,
      germinated.eventId,
    );
  }
</script>

<ActionFrame title="Germinated" {problem} {onCancel} {container}>
  <p>
    How many are up? Count them, or give your best guess. It becomes the number
    in the tray until they're counted.
  </p>
  <Stepper bind:value={up} min={0} presets={[20, 50, 100, 200]} />
  {#if guess?.forThisBatch != null && Number(guess.forThisBatch) > 0}
    <button
      class="secondary"
      onclick={() => {
        up = Number(guess!.forThisBatch);
        rough = true;
      }}
      >Use about {guess.forThisBatch}: earlier sowings gave {guess.perGram} a gram</button
    >
  {/if}
  <label class="tick">
    <input type="checkbox" bind:checked={rough} />
    This is just a rough guess, so ask how many when they're pricked out
  </label>
  <button disabled={busy || up < 1} onclick={() => record(true)}
    >Save this number</button
  >
  <button class="secondary" disabled={busy} onclick={() => record(false)}
    >Not sure yet</button
  >
</ActionFrame>
