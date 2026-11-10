<script lang="ts">
  import { rememberedPlaces } from "../../lib/offline";
  import { recordWork } from "../../lib/queue";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import {
    findPlaceByCode,
    isPlaceCode,
    trayPlaces,
    type Place,
  } from "../../lib/places";
  import { isRecorded, outcomeProblem, work } from "../../lib/work";
  import { signal } from "../../lib/feedback";
  import ActionFrame from "./ActionFrame.svelte";
  import Camera from "./Camera.svelte";

  let {
    container,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  let all = $state<Place[]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let code = $state("");
  let scanBox = $state<HTMLInputElement | null>(null);
  let camera = $state(false);
  const cameraAvailable = !!navigator.mediaDevices?.getUserMedia;
  let problem = $state("");
  let busy = $state(false);

  onMount(async () => {
    try {
      all = (await api.GET("/api/locations")).data ?? [];
    } catch {
      all = rememberedPlaces() as Place[];
    }

    places = trayPlaces(all).filter(
      (place) => place.id !== container.locationId,
    );

    // Ready for a USB scanner straight away.
    scanBox?.focus();
  });

  async function move(place: { id: string; label: string }) {
    busy = true;
    const outcome = await recordWork(
      work.move(container.id, { locationId: place.id }),
    );
    busy = false;

    if (isRecorded(outcome)) {
      onDone(`Moved ${container.labelCode} to ${place.label}`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }

  function scanned(event: SubmitEvent) {
    event.preventDefault();
    take(code);
    code = "";
  }

  function take(label: string) {
    const place = isPlaceCode(label) ? findPlaceByCode(label, all) : undefined;
    const option = places.find((item) => item.id === place?.id);
    signal(!!option);

    if (option) {
      void move(option);
    } else if (place && place.id === container.locationId) {
      problem = `${container.labelCode} is already on ${place.path}.`;
    } else if (place?.kind === "Site") {
      problem = `${place.name} is a whole site. Scan a bench or bed in it.`;
    } else {
      problem = "That isn't the label of a place.";
    }
  }
</script>

<ActionFrame title="Move to" {problem} {onCancel} {container}>
  <form class="stack" onsubmit={scanned}>
    <label>
      Scan the place's label
      <input
        bind:this={scanBox}
        bind:value={code}
        autocomplete="off"
        autocapitalize="characters"
      />
    </label>
  </form>
  {#if camera}
    <Camera
      continuous
      onScan={(scannedCode) => take(scannedCode)}
      onCancel={() => (camera = false)}
    />
  {:else if cameraAvailable}
    <button class="secondary" onclick={() => (camera = true)}>Use camera</button
    >
  {/if}
  <p class="muted">Or choose:</p>
  <div class="stack">
    {#each places as place (place.id)}
      <button class="secondary" disabled={busy} onclick={() => move(place)}
        >{place.label}</button
      >
    {/each}
  </div>
</ActionFrame>
