<script lang="ts">
  import type { Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";

  // Only shown for a mixed tray: a single batch needs no choosing.
  let {
    lines,
    selected = $bindable(),
  }: {
    lines: Schemas["ContainerLine"][];
    selected: string | null;
  } = $props();
</script>

{#if lines.length > 1}
  <div class="stack">
    <p>Which plants?</p>
    {#each lines as line (line.batchId)}
      <button
        type="button"
        class={selected === line.batchId ? "" : "secondary"}
        aria-pressed={selected === line.batchId}
        onclick={() => (selected = line.batchId)}
      >
        {plantName(line.commonName, line.variety)} ({line.quantity})
      </button>
    {/each}
  </div>
{/if}
