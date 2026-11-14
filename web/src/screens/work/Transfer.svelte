<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { nextPotSize, quantityPresets } from "../../lib/tray";
  import type { Schemas } from "../../lib/types";
  import {
    countedSinceNote,
    outcomeProblem,
    plantName,
    submit,
    retainIdentity,
    work,
    type WorkRequest,
  } from "../../lib/work";
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

  // From a tray nobody counted, or only roughly, the count is made here: how many taken and how many left.
  const counting = $derived(line.notCounted || !!line.rough);
  // Usually the whole lot moves on, into the next size up. From a rough guess, start from the guess.
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

  let pending: WorkRequest | null = null;
  async function record() {
    busy = true;
    pending = retainIdentity(
      work.transfer(
        container.id,
        {
          type,
          batchId: line.batchId,
          quantity,
          containerKind: kind,
          potSizeId: potSizeId || null,
          locationId: null,
          leftInTray: counting ? left : null,
          countEventId: counting ? countEventId : null,
        },
        when,
      ),
      pending,
    );
    const outcome = await submit(pending);
    busy = false;

    if (outcome.kind === "stored") {
      onDone(
        `${verb} ${quantity} ${plantName(line.commonName, line.variety)}. New label: ${outcome.labelCode}.${countedSinceNote(outcome)}`,
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
      {#if counting}
        <p>
          How many did you {type === "PottedOn" ? "pot on" : "prick out"}? Count
          as you go: {line.rough
            ? `there were about ${line.quantity}, and this count replaces that guess.`
            : "this is the first count of these."}
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
          Potting on needs a connection, to make the new label. Try again when
          you have signal.
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
      {#if counting && left === null}
        <p class="muted">Say whether any are left in the tray.</p>
      {/if}
      <button
        disabled={busy || potSizesUnavailable || (counting && left === null)}
        onclick={record}>Done</button
      >
    </div>
  </div>
</ActionFrame>
