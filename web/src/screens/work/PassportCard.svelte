<script lang="ts">
  import type { Snippet } from "svelte";

  // A plant passport drawn the way it's printed: "UK Plant Passport" over its lettered parts, in its own border.
  let {
    partA,
    partB,
    partC = null,
    partD,
    partE = null,
    caption,
    children,
  }: {
    partA: string | null;
    partB: string | null;
    partC?: string | null;
    partD: string | null;
    partE?: string | null;
    /** Read out first, so a screen reader knows which passport this is. */
    caption: string;
    children?: Snippet;
  } = $props();

  const parts = $derived([
    { key: "A", value: partA, botanical: true },
    { key: "B", value: partB, botanical: false },
    { key: "C", value: partC, botanical: false },
    { key: "D", value: partD, botanical: false },
    { key: "E", value: partE, botanical: false },
  ]);
</script>

<figure class="passport">
  <figcaption class="visually-hidden">{caption}</figcaption>
  <p class="passport-title" aria-hidden="true">UK Plant Passport</p>
  <dl>
    {#each parts as part (part.key)}
      <dt>{part.key}</dt>
      <dd
        class:botanical={part.botanical}
        class:missing={!part.value && part.key !== "E"}
      >
        {part.value || (part.key === "E" ? "" : "missing")}
      </dd>
    {/each}
  </dl>
  {#if children}
    <div class="passport-foot">{@render children()}</div>
  {/if}
</figure>
