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

  let all = $state<Place[]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let code = $state("");
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
    const place = isPlaceCode(code) ? findPlaceByCode(code, all) : undefined;
    const option = places.find((item) => item.id === place?.id);
    code = "";

    if (option) {
      void move(option);
    } else {
      problem = "That isn't the label of a place this can go.";
    }
  }
</script>

<ActionFrame title="Move to" {problem} {onCancel}>
  <form class="stack" onsubmit={scanned}>
    <label>
      Scan the place's label
      <input bind:value={code} autocomplete="off" autocapitalize="characters" />
    </label>
  </form>
  <p class="muted">Or choose:</p>
  <div class="stack">
    {#each places as place (place.id)}
      <button class="secondary" disabled={busy} onclick={() => move(place)}
        >{place.label}</button
      >
    {/each}
  </div>
</ActionFrame>
