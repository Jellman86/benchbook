<script lang="ts">
  import {
    isManageSectionOn,
    visibleManageGroups,
    type ManageSection,
  } from "../lib/sections";
  import type { BatchPrefill } from "../lib/work";
  import type { AskAbout } from "../lib/assistant.svelte";
  import Climate from "./manage/Climate.svelte";
  import Consignments from "./manage/Consignments.svelte";
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
    onAsk,
  }: {
    section: ManageSection | null;
    batchId?: string | null;
    onSection: (section: ManageSection | null) => void;
    onTrace: (batchId: string | null) => void;
    onBack: () => void;
    onStartBatch: (prefill: BatchPrefill) => void;
    onOpenTray: (label: string) => void;
    onMoveTraysHere: (id: string) => void;
    /** Asks Ask Hortiplanner about something, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
  } = $props();

  const back = () => onSection(null);
  // A section whose part of Hortiplanner is turned off isn't opened, even from a remembered screen.
  const open = $derived(section && isManageSectionOn(section) ? section : null);
  const groups = $derived(visibleManageGroups());
</script>

{#if open === "figures"}
  <Figures onBack={back} />
{:else if open === "money"}
  <Money onBack={back} />
{:else if open === "purchases"}
  <Purchases onBack={back} {onStartBatch} />
{:else if open === "plants"}
  <Plants onBack={back} {onAsk} />
{:else if open === "suppliers"}
  <Suppliers onBack={back} />
{:else if open === "places"}
  <Locations onBack={back} {onOpenTray} {onMoveTraysHere} />
{:else if open === "mothers"}
  <MotherPlants onBack={back} {onTrace} />
{:else if open === "climate"}
  <Climate onBack={back} />
{:else if open === "jobs"}
  <Jobs onBack={back} />
{:else if open === "trace"}
  <Trace {batchId} onChoose={onTrace} onBack={back} {onOpenTray} />
{:else if open === "invoices"}
  <Invoices onBack={back} />
{:else if open === "consignments"}
  <Consignments onBack={back} />
{:else if open === "customers"}
  <Customers onBack={back} />
{:else}
  <main>
    <h1>Manage</h1>
    {#each groups as group (group.heading)}
      <section class="stack menu-group">
        <h2 class="settings-heading">{group.heading}</h2>
        <div class="menu-grid">
          {#each group.items as item (item.section)}
            <button
              class="secondary menu-item"
              onclick={() => onSection(item.section)}
            >
              <item.icon size={20} />
              {item.label}
            </button>
          {/each}
        </div>
      </section>
    {/each}
    <p class="muted">
      The nursery's details, labels, pot sizes, staff and signing in are under
      Settings.
    </p>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
