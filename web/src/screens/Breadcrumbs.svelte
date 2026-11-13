<script lang="ts">
  import ChevronRight from "@lucide/svelte/icons/chevron-right";
  import { trail } from "../lib/crumbs.svelte";

  // Where the person is, from the top level down. Every level but the last can be tapped to go back to it.
  const items = $derived(trail());
</script>

{#if items.length > 1}
  <nav class="breadcrumbs" aria-label="Breadcrumb">
    <ol>
      {#each items as item, index (index)}
        <li>
          {#if index > 0}<ChevronRight size={14} aria-hidden="true" />{/if}
          {#if index < items.length - 1 && item.go}
            <a
              class="crumb"
              href="/"
              onclick={(event) => {
                event.preventDefault();
                item.go?.();
              }}>{item.label}</a
            >
          {:else}
            <span
              class="crumb"
              aria-current={index === items.length - 1 ? "page" : undefined}
              >{item.label}</span
            >
          {/if}
        </li>
      {/each}
    </ol>
  </nav>
{/if}
