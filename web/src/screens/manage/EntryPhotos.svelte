<script lang="ts">
  import { shrinkPhoto } from "../../lib/photo";

  let {
    entry,
    id,
    photoIds,
    gallery = false,
    onChanged,
    onProblem,
  }: {
    entry: "PotSize" | "Location";
    id: string;
    photoIds: string[];
    /** Large photos in a grid, for a page about the place, rather than small ones in a row. */
    gallery?: boolean;
    onChanged: () => Promise<void>;
    onProblem: (message: string) => void;
  } = $props();

  let busy = $state(false);

  async function add(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    busy = true;

    try {
      const response = await fetch(`/api/catalogue-photos/${entry}/${id}`, {
        method: "POST",
        body: await shrinkPhoto(file),
      });

      input.value = "";

      if (response.ok) {
        await onChanged();
      } else {
        onProblem("That photo couldn't be saved. Please use a photo.");
      }
    } catch {
      onProblem("Can't reach GrowLog. Check the connection and try again.");
    } finally {
      busy = false;
    }
  }

  async function remove(photoId: string) {
    if (!confirm("Remove this photo?")) {
      return;
    }

    try {
      const response = await fetch(`/api/catalogue-photos/${photoId}`, {
        method: "DELETE",
      });

      if (response.ok) {
        await onChanged();
      } else {
        onProblem("That photo couldn't be removed.");
      }
    } catch {
      onProblem("Can't reach GrowLog. Check the connection and try again.");
    }
  }
</script>

<div class="entry-photos" class:gallery>
  {#each photoIds as photoId (photoId)}
    <span class="entry-photo">
      <a href="/api/catalogue-photos/{photoId}" target="_blank" rel="noopener">
        <img
          class="plant-thumb"
          src="/api/catalogue-photos/{photoId}"
          alt={entry === "PotSize" ? "The pot" : "The place"}
          loading="lazy"
        />
      </a>
      <button
        type="button"
        class="photo-remove"
        aria-label="Remove photo"
        onclick={() => remove(photoId)}>×</button
      >
    </span>
  {/each}
  <label class="photo-choice photo-add">
    <span>{busy ? "Saving…" : "Add photo"}</span>
    <input type="file" accept="image/*" disabled={busy} onchange={add} />
  </label>
</div>
