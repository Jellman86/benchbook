<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    photoCaption,
    photoKinds,
    type BatchPhotoKind,
  } from "../../lib/batchPhotos";
  import { shrinkPhoto } from "../../lib/photo";
  import type { Schemas } from "../../lib/types";

  // A batch's photos as a strip, oldest first, each with the day of the batch and the stage it had reached.
  // The kinds offered can be added; a wrong photo can be removed by whoever took it, or the owner.
  let {
    batchId,
    containerId = null,
    kinds = [],
    onAsk,
  }: {
    batchId: string;
    containerId?: string | null;
    kinds?: BatchPhotoKind[];
    /** Asks Ask GrowLog about a photo, when this person can ask. */
    onAsk?: (photoId: string) => void;
  } = $props();

  type Photo = Schemas["BatchPhotoSummary"];

  let photos = $state<Photo[]>([]);
  let viewing = $state<Photo | null>(null);
  let adding = $state(false);
  let problem = $state("");
  let dialog = $state<HTMLDialogElement>();

  const choices = $derived(
    photoKinds.filter((choice) => kinds.includes(choice.kind)),
  );

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

  async function add(event: Event, kind: BatchPhotoKind) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    adding = true;
    problem = "";

    try {
      const at = containerId ? `&containerId=${containerId}` : "";
      const response = await fetch(
        `/api/batches/${batchId}/photos?kind=${kind}${at}`,
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

  function view(photo: Photo) {
    viewing = photo;
    dialog?.showModal();
  }

  function close() {
    dialog?.close();
    viewing = null;
  }

  async function remove(photo: Photo) {
    if (!confirm("Remove this photo? It can't be brought back.")) {
      return;
    }

    try {
      const response = await fetch(`/api/batch-photos/${photo.id}`, {
        method: "DELETE",
      });
      problem = response.ok ? "" : "That photo couldn't be removed.";
      close();
      await load();
    } catch {
      problem = "That photo couldn't be removed. Check the connection.";
    }
  }
</script>

{#if photos.length > 0}
  <ol class="photo-strip" aria-label="Photos of this batch">
    {#each photos as photo (photo.id)}
      <li>
        <button class="photo-strip-item" onclick={() => view(photo)}>
          <img src="/api/batch-photos/{photo.id}" alt="" loading="lazy" />
          <span>{photoCaption(photo)}</span>
        </button>
      </li>
    {/each}
  </ol>
{/if}

<dialog
  class="photo-viewer"
  bind:this={dialog}
  onclose={() => (viewing = null)}
  aria-label="Photo"
>
  {#if viewing}
    <img src="/api/batch-photos/{viewing.id}" alt={photoCaption(viewing)} />
    <p><strong>{photoCaption(viewing)}</strong></p>
    <div class="row">
      <button onclick={close}>Close</button>
      {#if onAsk}
        <button
          class="secondary"
          onclick={() => {
            const id = viewing!.id;
            close();
            onAsk(id);
          }}>Ask about this photo</button
        >
      {/if}
      {#if viewing.canRemove}
        <button class="secondary danger" onclick={() => remove(viewing!)}
          >Remove this photo</button
        >
      {/if}
    </div>
  {/if}
</dialog>

{#if choices.length > 0}
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
  </fieldset>
{/if}
{#if problem}<p class="problem" role="alert">{problem}</p>{/if}
