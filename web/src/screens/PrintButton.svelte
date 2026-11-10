<script lang="ts">
  import Printer from "@lucide/svelte/icons/printer";
  import { withCopies } from "../lib/labels";

  // Prints a label, or several copies of it at once.
  let { href, label }: { href: string; label: string } = $props();

  let copies = $state(1);
</script>

<span class="print-button">
  <a
    class="button secondary"
    href={withCopies(href, copies)}
    target="_blank"
    rel="noopener"><Printer size={18} />{label}</a
  >
  <select
    bind:value={copies}
    aria-label="How many copies of the {label.toLowerCase()}"
  >
    {#each [1, 2, 5, 10, 20, 50] as count (count)}
      <option value={count}>{count === 1 ? "1 copy" : `${count} copies`}</option
      >
    {/each}
  </select>
</span>
