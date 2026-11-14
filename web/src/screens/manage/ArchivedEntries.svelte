<script lang="ts">
  import Archive from "@lucide/svelte/icons/archive";
  import {
    listArchived,
    restore,
    type ArchivableList,
    type Archived,
  } from "../../lib/archive";

  let {
    list,
    onRestored,
  }: { list: ArchivableList; onRestored: () => void | Promise<void> } =
    $props();

  let open = $state(false);
  let entries = $state<Archived[]>([]);

  async function show() {
    entries = await listArchived(list);
    open = true;
  }

  async function bringBack(entry: Archived) {
    await restore(list, entry.id);
    entries = entries.filter((item) => item.id !== entry.id);
    await onRestored();
  }
</script>

{#if open}
  <section class="stack archived">
    <h2>No longer used</h2>
    {#each entries as entry (entry.id)}
      <div class="card recent">
        <span>{entry.name}</span>
        <div class="row">
          <button class="secondary" onclick={() => bringBack(entry)}
            >Use again</button
          >
        </div>
      </div>
    {:else}
      <p class="muted">There's nothing here at the moment.</p>
    {/each}
    <button class="link-button" onclick={() => (open = false)}
      >Hide these</button
    >
  </section>
{:else}
  <button class="link-button" onclick={show}
    ><Archive size={16} aria-hidden="true" />Show ones no longer used</button
  >
{/if}
