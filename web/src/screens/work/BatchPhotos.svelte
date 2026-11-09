<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";

  let { batchId }: { batchId: string } = $props();

  let photos = $state<Schemas["BatchPhotoSummary"][]>([]);

  onMount(async () => {
    try {
      photos =
        (
          await api.GET("/api/batches/{id}/photos", {
            params: { path: { id: batchId } },
          })
        ).data ?? [];
    } catch {
      // Photos aren't kept offline. Everything else on the screen still works.
    }
  });
</script>

{#if photos.length > 0}
  <div class="row">
    {#each photos as photo (photo.id)}
      <a href="/api/batch-photos/{photo.id}" target="_blank" rel="noopener">
        <img
          class="plant-thumb"
          src="/api/batch-photos/{photo.id}"
          alt="{photo.kind} photo"
          loading="lazy"
        />
      </a>
    {/each}
  </div>
{/if}
