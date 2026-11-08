<script lang="ts">
  import { rememberedPlaces } from "../../lib/offline";
  import { recordWork } from "../../lib/queue";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, work, workPlaces } from "../../lib/work";
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

  let places = $state<{ id: string; label: string }[]>([]);
  let problem = $state("");
  let busy = $state(false);

  onMount(async () => {
    let all;

    try {
      all = (await api.GET("/api/locations")).data ?? [];
    } catch {
      all = rememberedPlaces();
    }

    places = workPlaces(all).filter(
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
</script>

<ActionFrame title="Move to" {problem} {onCancel}>
  <div class="stack">
    {#each places as place (place.id)}
      <button class="secondary" disabled={busy} onclick={() => move(place)}
        >{place.label}</button
      >
    {/each}
  </div>
</ActionFrame>
