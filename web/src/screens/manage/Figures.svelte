<script lang="ts">
  import { lossWords } from "../../lib/losses";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Column } from "../../lib/table";
  import { attempt, type Schemas } from "../../lib/types";
  import { lossReasons, percent, plantName } from "../../lib/work";
  import BarList from "../BarList.svelte";
  import DataTable from "../DataTable.svelte";

  let { onBack }: { onBack: () => void } = $props();

  type PlantFigures = Schemas["PlantFigures"];
  type GroupFigures = Schemas["GroupFigures"];

  let figures = $state<Schemas["Figures"] | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  const reasonLabel = (reason: string) =>
    lossReasons.find((item) => item.reason === reason)?.label ?? reason;

  const rate = (value: number | string | null | undefined) =>
    value === null || value === undefined ? null : Number(value);

  /** "2026-11" as "Nov 2026". */
  const monthLabel = (name: string) => {
    const [year, month] = name.split("-").map(Number);
    return year && month
      ? new Date(year, month - 1, 1).toLocaleDateString("en-GB", {
          month: "short",
          year: "numeric",
        })
      : name;
  };

  // Seed sown "about so many" gives a rate that is only approximate, and says so.
  const germination = (
    value: number | string | null | undefined,
    approximate: boolean | undefined,
  ) =>
    approximate && value != null ? `about ${percent(value)}` : percent(value);

  const plantColumns: Column<PlantFigures>[] = [
    { label: "Plant", value: (row) => plantName(row.commonName, row.variety) },
    { label: "Started", value: (row) => Number(row.started), numeric: true },
    {
      label: "Germination",
      value: (row) => rate(row.germinationRate),
      text: (row) =>
        germination(row.germinationRate, row.germinationApproximate),
      numeric: true,
    },
    {
      label: "Survival",
      value: (row) => rate(row.survivalRate),
      text: (row) => percent(row.survivalRate),
      numeric: true,
    },
    { label: "Lost", value: (row) => Number(row.lost), numeric: true },
    {
      label: "Counted +/−",
      value: (row) => Number(row.adjusted),
      numeric: true,
    },
    { label: "Alive now", value: (row) => Number(row.alive), numeric: true },
  ];

  const groupColumns = (
    label: string,
    name = (row: GroupFigures) => row.name,
  ) =>
    [
      { label, value: name },
      { label: "Started", value: (row) => Number(row.started), numeric: true },
      {
        label: "Germination",
        value: (row) => rate(row.germinationRate),
        text: (row) =>
          germination(row.germinationRate, row.germinationApproximate),
        numeric: true,
      },
      {
        label: "Survival",
        value: (row) => rate(row.survivalRate),
        text: (row) => percent(row.survivalRate),
        numeric: true,
      },
      { label: "Lost", value: (row) => Number(row.lost), numeric: true },
    ] as Column<GroupFigures>[];

  const survivalBars = (
    groups: GroupFigures[],
    name = (row: GroupFigures) => row.name,
  ) =>
    groups
      .filter((group) => group.survivalRate !== null)
      .map((group) => ({
        label: name(group),
        value: Number(group.survivalRate) * 100,
        text: percent(group.survivalRate),
      }));

  const breakdowns = $derived(
    figures
      ? [
          {
            title: "Survival by supplier",
            heading: "Supplier",
            groups: figures.bySupplier,
            name: (row: GroupFigures) => row.name,
          },
          {
            title: "Survival by place",
            heading: "Place",
            groups: figures.byPlace,
            name: (row: GroupFigures) => row.name,
          },
          {
            title: "Survival by month started",
            heading: "Month",
            groups: figures.bySeason,
            name: (row: GroupFigures) => monthLabel(row.name),
          },
        ].filter((item) => item.groups.length > 0)
      : [],
  );

  onMount(() =>
    attempt(async () => {
      figures = (await api.GET("/api/figures")).data ?? null;
    }, setProblem),
  );
</script>

<main>
  <h1>Figures</h1>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if figures}
    {#if figures.plants.length === 0}
      <p class="note-empty muted">Nothing recorded yet.</p>
    {:else}
      <div class="desktop-only">
        <DataTable
          rows={figures.plants}
          columns={plantColumns}
          key={(row) => row.plantId}
          caption="Germination and survival for each plant"
        />
      </div>
      <div class="mobile-only">
        <div class="card-grid">
          {#each figures.plants as plant (plant.plantId)}
            <div class="card">
              <strong>{plantName(plant.commonName, plant.variety)}</strong>
              <span>Started: {plant.started}</span>
              <span
                >Germination: {germination(
                  plant.germinationRate,
                  plant.germinationApproximate,
                )}</span
              >
              <span>Survival: {percent(plant.survivalRate)}</span>
              <span>Lost: {plant.lost}</span>
              {#if Number(plant.adjusted) !== 0}
                <span>Stock count adjustments: {plant.adjusted}</span>
              {/if}
              <span>Alive now: {plant.alive}</span>
            </div>
          {/each}
        </div>
      </div>

      <div class="figure-grid">
        {#if figures.losses.length > 0}
          <section class="card">
            <h2>Losses by reason</h2>
            <BarList
              caption="Plants lost, by reason"
              series={3}
              rows={figures.losses.map((loss) => ({
                label: reasonLabel(String(loss.reason)),
                value: Number(loss.quantity),
                text: lossWords(
                  Number(loss.quantity),
                  Number(loss.uncountedTrays),
                ),
              }))}
            />
          </section>
        {/if}

        {#each breakdowns as item (item.title)}
          <section class="card">
            <h2>{item.title}</h2>
            <BarList
              caption={item.title}
              max={100}
              series={2}
              rows={survivalBars(item.groups, item.name)}
            />
            <details>
              <summary>See the numbers</summary>
              <DataTable
                rows={item.groups}
                columns={groupColumns(item.heading, item.name)}
                key={(row) => row.name}
                caption={item.title}
              />
            </details>
          </section>
        {/each}
      </div>
    {/if}
  {/if}

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
