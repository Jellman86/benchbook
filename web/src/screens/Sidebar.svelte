<script lang="ts">
  import LogOut from "@lucide/svelte/icons/log-out";
  import Monitor from "@lucide/svelte/icons/monitor";
  import Flower2 from "@lucide/svelte/icons/flower-2";
  import Moon from "@lucide/svelte/icons/moon";
  import PanelLeftClose from "@lucide/svelte/icons/panel-left-close";
  import PanelLeftOpen from "@lucide/svelte/icons/panel-left-open";
  import Plus from "@lucide/svelte/icons/plus";
  import ScanLine from "@lucide/svelte/icons/scan-line";
  import Sprout from "@lucide/svelte/icons/sprout";
  import Sun from "@lucide/svelte/icons/sun";
  import UserRound from "@lucide/svelte/icons/user-round";
  import type { Me } from "../lib/boot";
  import { manageSections, type ManageSection } from "../lib/sections";
  import { themes, type Theme } from "../lib/theme";

  // Only shown on wide screens: phones get the bottom bar instead.
  let {
    me,
    current,
    collapsed,
    theme,
    onHome,
    onNewBatch,
    onProduction,
    onPlants,
    onSection,
    onToggleCollapsed,
    onCycleTheme,
    onSettings,
    onFinished,
  }: {
    me: Me;
    current:
      | "home"
      | "start"
      | "more"
      | "production"
      | "plants"
      | ManageSection
      | null;
    collapsed: boolean;
    theme: Theme;
    onHome: () => void;
    onNewBatch: () => void;
    onProduction: () => void;
    onPlants: () => void;
    onSection: (section: ManageSection) => void;
    onToggleCollapsed: () => void;
    onCycleTheme: () => void;
    onSettings: () => void;
    onFinished: () => void;
  } = $props();

  const here = (item: typeof current) =>
    current === item ? ("page" as const) : undefined;

  const themeLabel = $derived(
    themes.find((item) => item.theme === theme)?.label ?? "",
  );
  const collapseLabel = $derived(
    collapsed ? "Expand sidebar" : "Collapse sidebar",
  );
</script>

<aside class="sidebar">
  <div class="brand">
    <img src="/logo-96.png" alt="" width="40" height="40" />
    <span class="nav-text">GrowLog</span>
  </div>

  <nav aria-label="Main">
    <button
      aria-current={here("home")}
      aria-label="Record work"
      title="Record work"
      onclick={onHome}
    >
      <ScanLine size={20} />
      <span class="nav-text">Record work</span>
    </button>
    <button
      aria-current={here("start")}
      aria-label="New batch"
      title="New batch"
      onclick={onNewBatch}
    >
      <Plus size={20} />
      <span class="nav-text">New batch</span>
    </button>

    <button
      aria-current={here("production")}
      aria-label="Production"
      title="Production"
      onclick={onProduction}
    >
      <Sprout size={20} />
      <span class="nav-text">Production</span>
    </button>

    {#if !me.isOwner}
      <button
        aria-current={here("plants")}
        aria-label="Plants"
        title="Plants"
        onclick={onPlants}
      >
        <Flower2 size={20} />
        <span class="nav-text">Plants</span>
      </button>
    {/if}

    {#if me.isOwner}
      <p class="nav-heading nav-text">Manage</p>
      <hr class="nav-rule" />
      {#each manageSections as item (item.section)}
        <button
          aria-current={here(item.section)}
          aria-label={item.label}
          title={item.label}
          onclick={() => onSection(item.section)}
        >
          <item.icon size={20} />
          <span class="nav-text">{item.label}</span>
        </button>
      {/each}
    {/if}
  </nav>

  <div class="sidebar-foot">
    <button
      aria-current={here("more")}
      aria-label="My settings"
      title="My settings"
      onclick={onSettings}
    >
      <UserRound size={20} />
      <span class="nav-text">{me.name}</span>
    </button>
    <button
      aria-label="Theme: {themeLabel}"
      title="Theme: {themeLabel}"
      onclick={onCycleTheme}
    >
      {#if theme === "light"}
        <Sun size={20} />
      {:else if theme === "dark"}
        <Moon size={20} />
      {:else}
        <Monitor size={20} />
      {/if}
      <span class="nav-text">{themeLabel}</span>
    </button>
    <button
      aria-label={collapseLabel}
      title={collapseLabel}
      onclick={onToggleCollapsed}
    >
      {#if collapsed}
        <PanelLeftOpen size={20} />
      {:else}
        <PanelLeftClose size={20} />
      {/if}
      <span class="nav-text">Collapse</span>
    </button>
    <button aria-label="Finished" title="Finished" onclick={onFinished}>
      <LogOut size={20} />
      <span class="nav-text">Finished</span>
    </button>
  </div>
</aside>
