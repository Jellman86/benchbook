<script lang="ts">
  import { isOn } from "../lib/features.svelte";
  import Clock from "@lucide/svelte/icons/clock";
  import PackageCheck from "@lucide/svelte/icons/package-check";
  import PoundSterling from "@lucide/svelte/icons/pound-sterling";
  import { onMount } from "svelte";
  import { api } from "../api/client";
  import {
    plantsByStage,
    stages,
    totalPlants,
    type ProductionRow,
  } from "../lib/production";
  import { byContainer } from "../lib/places";
  import type { Schemas } from "../lib/types";
  import { describeRecent, money } from "../lib/work";
  import BarList from "./BarList.svelte";

  // The owner's view of the whole nursery, for a wide screen.
  let { onOpen }: { onOpen: (label: string) => void } = $props();

  /** Seed usually shows within four weeks; longer is worth a look. */
  const slowToGerminateDays = 28;

  let rows = $state<ProductionRow[]>([]);
  let owed = $state(0);
  let today = $state<Schemas["RecentAction"][]>([]);
  let loaded = $state(false);

  const now = new Date();
  const daysSince = (since: string | null | undefined) =>
    since ? (now.getTime() - new Date(since).getTime()) / 86_400_000 : 0;

  const trays = $derived(byContainer(rows));
  const byStage = $derived(plantsByStage(rows));
  const stageBars = $derived(
    stages
      .filter((item) => item.always || (byStage.get(item.stage) ?? 0) > 0)
      .map((item) => ({
        label: item.label,
        value: byStage.get(item.stage) ?? 0,
      })),
  );
  const slow = $derived(
    trays.filter(
      (lines) =>
        lines[0].stage === "Sown" &&
        daysSince(lines[0].stageSince) >= slowToGerminateDays,
    ),
  );
  const ready = $derived(trays.filter((lines) => lines[0].stage === "Ready"));

  onMount(async () => {
    try {
      rows = (await api.GET("/api/production")).data ?? [];
      owed = isOn("Purchases")
        ? Number((await api.GET("/api/money/summary")).data?.owed ?? 0)
        : 0;
      const startOfDay = new Date(
        now.getFullYear(),
        now.getMonth(),
        now.getDate(),
      );
      today = (
        (
          await api.GET("/api/events/recent", {
            params: { query: { limit: 50, everyone: true } },
          })
        ).data ?? []
      ).filter((action) => new Date(action.occurredAt) >= startOfDay);
    } finally {
      loaded = true;
    }
  });
</script>

{#if loaded}
  <section class="stack overview" aria-label="The nursery at a glance">
    <div class="stat-tiles">
      <div class="stat-tile">
        <strong>{totalPlants(rows)}</strong><span>plants growing</span>
      </div>
      <div class="stat-tile">
        <strong>{trays.length}</strong><span>trays and pot groups</span>
      </div>
      <div class="stat-tile">
        <strong>{ready.length}</strong><span>ready</span>
      </div>
      {#if isOn("Purchases")}
        <div class="stat-tile">
          <strong>{money(owed)}</strong><span>owed to suppliers</span>
        </div>
      {/if}
    </div>

    {#if rows.length > 0}
      <div class="card">
        <h2>Plants at each stage</h2>
        <BarList rows={stageBars} caption="Plants at each stage" />
      </div>
    {/if}

    {#if slow.length > 0 || ready.length > 0 || owed > 0}
      <div class="card">
        <h2>Worth a look</h2>
        <ul class="attention">
          {#if slow.length > 0}
            <li>
              <Clock size={18} />
              <span>
                {slow.length} sown {slow.length === 1
                  ? "tray hasn't"
                  : "trays haven't"}
                germinated after {slowToGerminateDays} days:
                {#each slow.slice(0, 6) as lines, index (lines[0].containerId)}
                  {index > 0 ? ", " : ""}<button
                    class="link-button"
                    onclick={() => onOpen(lines[0].labelCode)}
                    >{lines[0].labelCode}</button
                  >
                {/each}
              </span>
            </li>
          {/if}
          {#if ready.length > 0}
            <li>
              <PackageCheck size={18} />
              <span
                >{ready.length}
                {ready.length === 1 ? "tray is" : "trays are"} ready to sell</span
              >
            </li>
          {/if}
          {#if owed > 0}
            <li>
              <PoundSterling size={18} />
              <span>{money(owed)} is still owed to suppliers</span>
            </li>
          {/if}
        </ul>
      </div>
    {/if}

    <div class="card">
      <h2>Today across the nursery</h2>
      {#each today as action (action.id)}
        <p>
          {describeRecent(action)}<span class="muted"
            >{action.recordedBy ? ` · ${action.recordedBy}` : ""}</span
          >
        </p>
      {:else}
        <p class="muted">Nothing has been recorded yet today.</p>
      {/each}
    </div>
  </section>
{/if}
