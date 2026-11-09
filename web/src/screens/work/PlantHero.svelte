<script lang="ts">
  import Flower2 from "@lucide/svelte/icons/flower-2";
  import { shortCredit } from "../../lib/work";

  // Decorative: the plant's name is always written next to it.
  let { url, credit }: { url?: string | null; credit?: string | null } =
    $props();

  let failedUrl = $state<string | null>(null);
</script>

<figure class="plant-hero">
  {#if url && failedUrl !== url}
    <img src={url} alt="" onerror={() => (failedUrl = url)} />
  {:else}
    <span class="plant-hero-empty"><Flower2 size={56} /></span>
  {/if}
  {#if credit}<figcaption class="muted credit">
      {shortCredit(credit)}
    </figcaption>{/if}
</figure>
