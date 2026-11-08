<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import { outcomeProblem, submit, work, workPlaces } from "../../lib/work";
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
    const { data } = await api.GET("/api/locations");
    places = workPlaces(data ?? []).filter(
      (place) => place.id !== container.locationId,
    );
  });

  async function move(place: { id: string; label: string }) {
    busy = true;
    const outcome = await submit(
      work.move(container.id, { locationId: place.id }),
    );
    busy = false;

    if (outcome.kind === "stored") {
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
