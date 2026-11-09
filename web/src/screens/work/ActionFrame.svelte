<script lang="ts">
  import type { Snippet } from "svelte";

  let {
    title,
    problem,
    onCancel,
    children,
  }: {
    title: string;
    problem: string;
    onCancel: () => void;
    children: Snippet;
  } = $props();
</script>

<main>
  <h1>{title}</h1>
  {@render children()}
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button class="back" onclick={onCancel}>Back</button>
</main>
