<script lang="ts">
  import ChevronRight from "@lucide/svelte/icons/chevron-right";
  import { trail } from "../lib/crumbs.svelte";

  // Where the person is, from the top level down. Every level but the last can be tapped to go back to it.
  const items = $derived(trail());

  // On a narrow screen a long trail scrolls to show where the person is, and fades at the start to show there's more.
  let list = $state<HTMLOListElement | null>(null);
  let cut = $state(false);

  $effect(() => {
    void items.map((item) => item.label).join("/");

    if (!list) {
      return;
    }

    list.scrollLeft = list.scrollWidth;
    cut = list.scrollWidth > list.clientWidth + 1;
  });
</script>

{#if items.length > 1}
  <nav class="breadcrumbs" aria-label="Breadcrumb">
    <ol
      bind:this={list}
      class:cut
      onscroll={() => (cut = !!list && list.scrollLeft > 1)}
    >
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
