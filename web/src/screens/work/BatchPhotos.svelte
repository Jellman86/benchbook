<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import type { Schemas } from "../../lib/types";

  // A batch's photos. Where adding is allowed, a packet, label or packaging photo can be added at any time.
  let { batchId, canAdd = false }: { batchId: string; canAdd?: boolean } =
    $props();

  const choices: { kind: Schemas["BatchPhotoKind"]; label: string }[] = [
    { kind: "Packet", label: "Seed packet" },
    { kind: "Label", label: "Label" },
    { kind: "Packaging", label: "Packaging" },
  ];

  let photos = $state<Schemas["BatchPhotoSummary"][]>([]);
  let adding = $state(false);
  let problem = $state("");

  async function load() {
    photos =
      (
        await api.GET("/api/batches/{id}/photos", {
          params: { path: { id: batchId } },
        })
      ).data ?? [];
  }

  onMount(async () => {
    try {
      await load();
    } catch {
      // Photos aren't kept offline. Everything else on the screen still works.
    }
  });

  async function add(event: Event, kind: Schemas["BatchPhotoKind"]) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    adding = true;
    problem = "";

    try {
      const response = await fetch(
        `/api/batches/${batchId}/photos?kind=${kind}`,
        { method: "POST", body: await shrinkPhoto(file) },
      );
      problem = response.ok ? "" : "That photo couldn't be saved.";
      await load();
    } catch {
      problem = "That photo couldn't be saved. Check the connection.";
    } finally {
      adding = false;
      input.value = "";
    }
  }
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

{#if canAdd}
  <fieldset class="stack tight batch-photo-add">
    <legend class="field-label">Add a photo</legend>
    <div class="card-grid">
      {#each choices as choice (choice.kind)}
        <label class="photo-choice" aria-disabled={adding}>
          <span class="plant-thumb placeholder"><Camera size={22} /></span>
          <span>{choice.label}</span>
          <input
            type="file"
            accept="image/*"
            disabled={adding}
            onchange={(event) => add(event, choice.kind)}
          />
        </label>
      {/each}
    </div>
    {#if adding}<p class="muted" role="status">Saving the photo…</p>{/if}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  </fieldset>
{/if}
