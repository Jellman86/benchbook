<script lang="ts">
  import type { Me } from "../lib/boot";
  import { manageSections, type ManageSection } from "../lib/sections";

  // Only shown on wide screens: phones and bench stations keep one thing at a time.
  let {
    me,
    current,
    onHome,
    onNewBatch,
    onSection,
    onFinished,
  }: {
    me: Me;
    current: "home" | "start" | ManageSection | null;
    onHome: () => void;
    onNewBatch: () => void;
    onSection: (section: ManageSection) => void;
    onFinished: () => void;
  } = $props();

  const here = (item: typeof current) =>
    current === item ? ("page" as const) : undefined;
</script>

<aside class="sidebar">
  <div class="brand">
    <img src="/logo-96.png" alt="" width="48" height="48" />
    <span>GrowLog</span>
  </div>

  <nav aria-label="Main">
    <button aria-current={here("home")} onclick={onHome}>Record work</button>
    <button aria-current={here("start")} onclick={onNewBatch}>New batch</button>

    {#if me.isOwner}
      <p class="nav-heading">Manage</p>
      {#each manageSections as item (item.section)}
        <button
          aria-current={here(item.section)}
          onclick={() => onSection(item.section)}>{item.label}</button
        >
      {/each}
    {/if}
  </nav>

  <div class="sidebar-foot">
    <span>{me.name}</span>
    <button onclick={onFinished}>Finished</button>
  </div>
</aside>
