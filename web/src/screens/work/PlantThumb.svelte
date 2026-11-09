<script lang="ts">
  import Flower2 from "@lucide/svelte/icons/flower-2";

  // Decorative: the plant's name is always written next to it.
  let { url }: { url?: string | null } = $props();

  let failedUrl = $state<string | null>(null);
</script>

{#if url && failedUrl !== url}
  <img
    class="plant-thumb"
    src={url}
    alt=""
    loading="lazy"
    onerror={() => (failedUrl = url)}
  />
{:else}
  <span class="plant-thumb placeholder"><Flower2 size={26} /></span>
{/if}
