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
    type,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
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

  // Usually the whole lot moves on, into the next size up.
  let quantity = $derived(Number(line.quantity));

  onMount(async () => {
    potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
    potSizeId = nextPotSize(potSizes, container.potSizeId);
  });

  async function record() {
    busy = true;
    const outcome = await submit(
      work.transfer(container.id, {
        type,
        batchId: line.batchId,
        quantity,
        containerKind: kind,
        potSizeId: potSizeId || null,
        locationId: null,
      }),
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
>
  <BatchChoice lines={container.contents} bind:selected={chosen} />
  <p>How many?</p>
  <Stepper
    bind:value={quantity}
    max={Number(line.quantity)}
    presets={quantityPresets(Number(line.quantity))}
  />
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
  <button disabled={busy} onclick={record}>Done</button>
</ActionFrame>
