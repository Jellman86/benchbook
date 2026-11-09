<script lang="ts">
  import Ellipsis from "@lucide/svelte/icons/ellipsis";
  import Plus from "@lucide/svelte/icons/plus";
  import ScanLine from "@lucide/svelte/icons/scan-line";
  import Settings from "@lucide/svelte/icons/settings";
  import type { Me } from "../lib/boot";

  // The phone's main navigation, within reach of a thumb.
  let {
    me,
    current,
    onHome,
    onNewBatch,
    onManage,
    onMore,
  }: {
    me: Me;
    current: "home" | "start" | "manage" | "more" | null;
    onHome: () => void;
    onNewBatch: () => void;
    onManage: () => void;
    onMore: () => void;
  } = $props();

  const here = (item: typeof current) =>
    current === item ? ("page" as const) : undefined;
</script>

<nav class="bottom-nav" aria-label="Phone">
  <button aria-current={here("home")} onclick={onHome}>
    <ScanLine size={24} />
    <span>Record</span>
  </button>
  <button aria-current={here("start")} onclick={onNewBatch}>
    <Plus size={24} />
    <span>New batch</span>
  </button>
  {#if me.isOwner}
    <button aria-current={here("manage")} onclick={onManage}>
      <Settings size={24} />
      <span>Manage</span>
    </button>
  {/if}
  <button aria-current={here("more")} onclick={onMore}>
    <Ellipsis size={24} />
    <span>More</span>
  </button>
</nav>
