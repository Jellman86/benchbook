<script lang="ts">
  import { manageGroups, type ManageSection } from "../lib/sections";
  import type { BatchPrefill } from "../lib/work";
  import Customers from "./manage/Customers.svelte";
  import Figures from "./manage/Figures.svelte";
  import Invoices from "./manage/Invoices.svelte";
  import Locations from "./manage/Locations.svelte";
  import Money from "./manage/Money.svelte";
  import Plants from "./manage/Plants.svelte";
  import Purchases from "./manage/Purchases.svelte";
  import Suppliers from "./manage/Suppliers.svelte";
  import Trace from "./manage/Trace.svelte";
  import Jobs from "./manage/Jobs.svelte";
  import MotherPlants from "./manage/MotherPlants.svelte";

  let {
    section,
    batchId = null,
    onSection,
    onTrace,
    onBack,
    onStartBatch,
    onOpenTray,
    onMoveTraysHere,
  }: {
    section: ManageSection | null;
    batchId?: string | null;
    onSection: (section: ManageSection | null) => void;
    onTrace: (batchId: string | null) => void;
    onBack: () => void;
    onStartBatch: (prefill: BatchPrefill) => void;
    onOpenTray: (label: string) => void;
    onMoveTraysHere: (id: string) => void;
  } = $props();

  const back = () => onSection(null);
</script>

{#if section === "figures"}
  <Figures onBack={back} />
{:else if section === "money"}
  <Money onBack={back} />
{:else if section === "purchases"}
  <Purchases onBack={back} {onStartBatch} />
{:else if section === "plants"}
  <Plants onBack={back} />
{:else if section === "suppliers"}
  <Suppliers onBack={back} />
{:else if section === "places"}
  <Locations onBack={back} {onOpenTray} {onMoveTraysHere} />
{:else if section === "mothers"}
  <MotherPlants onBack={back} {onTrace} />
{:else if section === "jobs"}
  <Jobs onBack={back} />
{:else if section === "trace"}
  <Trace {batchId} onChoose={onTrace} onBack={back} {onOpenTray} />
{:else if section === "invoices"}
  <Invoices onBack={back} />
{:else if section === "customers"}
  <Customers onBack={back} />
{:else}
  <main>
    <h1>Manage</h1>
    <div class="stack">
      {#each manageGroups as group (group.heading)}
        <h2 class="settings-heading">{group.heading}</h2>
        {#each group.items as item (item.section)}
          <button
            class="secondary menu-item"
            onclick={() => onSection(item.section)}
          >
            <item.icon size={20} />
            {item.label}
          </button>
        {/each}
      {/each}
    </div>
    <p class="muted">
      The nursery's details, labels, pot sizes, staff and signing in are under
      Settings.
    </p>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
