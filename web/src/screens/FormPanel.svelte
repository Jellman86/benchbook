<script lang="ts">
  import type { Snippet } from "svelte";

  // On a phone a long form hides behind a button, so the list comes first.
  // On a wide screen it simply sits beside the list.
  let {
    label,
    open = $bindable(false),
    children,
  }: {
    label: string;
    open?: boolean;
    children: Snippet;
  } = $props();
</script>

<button
  type="button"
  class="secondary panel-toggle"
  aria-expanded={open}
  onclick={() => (open = !open)}>{open ? "Hide form" : label}</button
>
<div class="panel-body" class:closed={!open}>
  {@render children()}
</div>
