<script lang="ts">
  import Droplets from "@lucide/svelte/icons/droplets";
  import Thermometer from "@lucide/svelte/icons/thermometer";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    climateDays,
    degrees,
    percent,
    readingAge,
    type HourPoint,
  } from "../../lib/climate";
  import { dayLabel } from "../../lib/dates";
  import { attempt, type Schemas } from "../../lib/types";
  import ClimateChart from "./ClimateChart.svelte";

  // How warm and damp each place with a sensor is, now and over the last days.
  let { onBack }: { onBack: () => void } = $props();

  type Place = Schemas["PlaceClimate"];

  const spans = [
    { days: 1, label: "Today" },
    { days: 7, label: "7 days" },
    { days: 31, label: "31 days" },
  ];

  let places = $state<Place[]>([]);
  let loaded = $state(false);
  let opened = $state<Place | null>(null);
  let days = $state(7);
  let history = $state<Schemas["PlaceClimateHistory"] | null>(null);
  let problem = $state("");

  const now = new Date();
  const setProblem = (message: string) => (problem = message);

  onMount(() =>
    attempt(async () => {
      places = (await api.GET("/api/climate")).data ?? [];
      loaded = true;
    }, setProblem),
  );

  $effect(() => {
    const place = opened;
    const span = days;
    history = null;

    if (place) {
      void attempt(async () => {
        history =
          (
            await api.GET("/api/climate/{locationId}", {
              params: {
                path: { locationId: place.locationId },
                query: { days: span },
              },
            })
          ).data ?? null;
      }, setProblem);
    }
  });

  const hours = $derived(history?.hours ?? []);
  const temperature = $derived<HourPoint[]>(
    hours
      .filter((hour) => hour.temperature != null)
      .map((hour) => ({
        at: new Date(hour.hour).getTime() + 1_800_000,
        value: Number(hour.temperature),
        low: Number(hour.lowestTemperature),
        high: Number(hour.highestTemperature),
      })),
  );
  const humidity = $derived<HourPoint[]>(
    hours
      .filter((hour) => hour.humidity != null)
      .map((hour) => ({
        at: new Date(hour.hour).getTime() + 1_800_000,
        value: Number(hour.humidity),
      })),
  );
  const since = $derived(history ? new Date(history.since).getTime() : 0);
  const table = $derived(climateDays(hours));
</script>

{#if opened}
  {@const place = opened}
  <main>
    <h1>{place.place}</h1>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <div class="segmented" role="group" aria-label="How far back">
      {#each spans as span (span.days)}
        <button
          class={days === span.days ? "" : "secondary"}
          aria-pressed={days === span.days}
          onclick={() => (days = span.days)}>{span.label}</button
        >
      {/each}
    </div>
    {#if history}
      <div class="card stack">
        <ClimateChart
          title="Temperature"
          points={temperature}
          {since}
          until={now.getTime()}
          format={(value) =>
            `${Number.isInteger(value) ? value : value.toFixed(1)}°`}
          colour="var(--chart-warm)"
        />
        <ClimateChart
          title="Humidity"
          points={humidity}
          {since}
          until={now.getTime()}
          format={(value) => `${Math.round(value)}%`}
          colour="var(--chart-cool)"
        />
        <p class="muted">
          The line is each hour's average; the shading runs from the hour's
          lowest to its highest. A gap means nothing was heard then.
        </p>
      </div>
      {#if table.length > 0}
        <details class="card">
          <summary>Day by day</summary>
          <table class="climate-table">
            <thead>
              <tr>
                <th>Day</th>
                <th>Lowest</th>
                <th>Highest</th>
                <th>Humidity</th>
              </tr>
            </thead>
            <tbody>
              {#each table as row (row.day)}
                <tr>
                  <td>{dayLabel(row.day, now)}</td>
                  <td>{degrees(row.lowest)}</td>
                  <td>{degrees(row.highest)}</td>
                  <td>{percent(row.humidity)}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </details>
      {/if}
    {:else}
      <p class="muted">Loading…</p>
    {/if}
    <button class="back" onclick={() => (opened = null)}>Back</button>
  </main>
{:else}
  <main>
    <h1>Climate</h1>
    <p class="muted">
      How warm and damp each place is, from sensors sending readings in. The
      owner adds sensors under Settings, Sensors.
    </p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if loaded && places.length === 0}
      <p class="muted">No sensors yet.</p>
    {/if}
    <div class="stack">
      {#each places as place (place.locationId)}
        {@const age = place.latest
          ? readingAge(place.latest.takenAt, now)
          : null}
        <button class="climate-card" onclick={() => (opened = place)}>
          <strong class="climate-place">{place.place}</strong>
          {#if place.latest}
            <span class="climate-now">
              <span
                ><Thermometer size={20} />
                <strong>{degrees(place.latest.temperature)}</strong></span
              >
              <span
                ><Droplets size={20} />
                <strong>{percent(place.latest.humidity)}</strong></span
              >
            </span>
            {#if place.today}
              <span class="muted"
                >Today {degrees(place.today.lowestTemperature)} to {degrees(
                  place.today.highestTemperature,
                )}</span
              >
            {/if}
            <span class={age?.stale ? "problem" : "muted"}
              >{age?.stale ? "Nothing heard since" : "Read"}
              {age?.words}</span
            >
          {:else}
            <span class="muted">No readings yet.</span>
          {/if}
        </button>
      {/each}
    </div>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
