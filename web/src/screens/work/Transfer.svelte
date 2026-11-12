<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { nextPotSize, quantityPresets } from "../../lib/tray";
  import type { Schemas } from "../../lib/types";
  import { outcomeProblem, plantName, submit, work } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
  import Stepper from "./Stepper.svelte";

  let {
    container,
    when,
    type,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    /** When the work was done, if not just now. */
    when?: string;
    type: "PottedOn" | "PrickedOut";
    onDone: (message: string, eventId: string, newLabel?: string) => void;
    onCancel: () => void;
  } = $props();

  let chosen = $state<string | null>(null);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);
  let potSizeId = $state("");
  let kind = $state<Schemas["ContainerKind"]>("PotGroup");
  let problem = $state("");
  let busy = $state(false);

  const verb = $derived(type === "PottedOn" ? "Potted on" : "Pricked out");
  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  // Usually the whole lot moves on, into the next size up. From a tray nobody counted, the count starts here.
  let quantity = $derived(line.notCounted ? 1 : Number(line.quantity));
  // For a tray nobody counted: how many are left in it, which with those taken makes its first count.
  let answer = $state<"none" | "some" | null>(null);
  let leftCount = $state(10);
  const left = $derived(
    answer === "none" ? 0 : answer === "some" ? leftCount : null,
  );
  const countEventId = crypto.randomUUID();

  let potSizesUnavailable = $state(false);

  // Pricking out usually goes into a tray of modules; potting on into a group of pots.
  // svelte-ignore state_referenced_locally
  kind = type === "PrickedOut" ? "Tray" : "PotGroup";

  onMount(async () => {
    try {
      potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
      potSizeId = nextPotSize(potSizes, container.potSizeId);
    } catch {
      potSizesUnavailable = true;
    }
  });

  async function record() {
    busy = true;
    const outcome = await submit(
      work.transfer(
        container.id,
        {
          type,
          batchId: line.batchId,
          quantity,
          containerKind: kind,
          potSizeId: potSizeId || null,
          locationId: null,
          leftInTray: line.notCounted ? left : null,
          countEventId: line.notCounted ? countEventId : null,
        },
        when,
      ),
    );
    busy = false;

    if (outcome.kind === "stored") {
      onDone(
        `${verb} ${quantity} ${plantName(line.commonName, line.variety)}. New label: ${outcome.labelCode}`,
        outcome.eventId,
        outcome.labelCode,
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame
  title={type === "PottedOn" ? "Pot on" : "Prick out"}
  {problem}
  {onCancel}
  {container}
>
  <div class="action-columns">
    <div class="stack">
      <BatchChoice lines={container.contents} bind:selected={chosen} />
      {#if line.notCounted}
        <p>
          How many did you {type === "PottedOn" ? "pot on" : "prick out"}? Count
          as you go: this is the first count of these.
        </p>
        <Stepper bind:value={quantity} presets={[24, 40, 60, 84]} />
        <p>Any left in the tray?</p>
        <div class="segmented two">
          <button
            aria-pressed={answer === "none"}
            onclick={() => (answer = "none")}>None</button
          >
          <button
            aria-pressed={answer === "some"}
            onclick={() => (answer = "some")}>Some left</button
          >
        </div>
        {#if answer === "some"}
          <p class="muted">Roughly how many are left?</p>
          <Stepper bind:value={leftCount} min={1} presets={[5, 10, 20]} />
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
      {#if potSizesUnavailable}
        <p class="problem">
          Potting on needs a connection, to make the new label. Try again with
          signal.
        </p>
      {/if}
      <label>
        New pot size
        <select bind:value={potSizeId}>
          {#each potSizes as size (size.id)}
            <option value={size.id}>{size.name}</option>
          {/each}
        </select>
      </label>
      <label>
        Going into
        <select bind:value={kind}>
          <option value="PotGroup">Group of pots</option>
          <option value="Tray">Tray</option>
          <option value="BenchLot">Bench lot</option>
        </select>
      </label>
      {#if line.notCounted && left === null}
        <p class="muted">Say whether any are left in the tray.</p>
      {/if}
      <button
        disabled={busy ||
          potSizesUnavailable ||
          (line.notCounted && left === null)}
        onclick={record}>Done</button
      >
    </div>
  </div>
</ActionFrame>
