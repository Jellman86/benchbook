<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { readingAge } from "../../lib/climate";
  import { attempt, type Schemas } from "../../lib/types";
  import { workPlaces } from "../../lib/work";

  // Temperature and humidity sensors, each in a place with its own key. The key is shown once, with what to put
  // in Home Assistant, and can be replaced if it's lost or seen by someone else.
  let { onBack }: { onBack: () => void } = $props();

  let sensors = $state<Schemas["SensorSummary"][]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let form = $state({ name: "", locationId: "" });
  let editing = $state<string | null>(null);
  let shown = $state<{ name: string; key: string } | null>(null);
  let problem = $state("");

  const now = new Date();
  const setProblem = (message: string) => (problem = message);
  const placeName = (id: string) =>
    places.find((place) => place.id === id)?.label ?? "A place no longer used";

  async function load() {
    sensors = (await api.GET("/api/sensors")).data ?? [];
  }

  onMount(() =>
    attempt(async () => {
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      await load();
    }, setProblem),
  );

  function reset() {
    editing = null;
    form = { name: "", locationId: "" };
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const body = { name: form.name.trim(), locationId: form.locationId };
      const name = body.name;

      if (editing) {
        const { response } = await api.PUT("/api/sensors/{id}", {
          params: { path: { id: editing } },
          body,
        });
        problem = response.ok
          ? ""
          : response.status === 409
            ? "Another sensor already has that name."
            : "Give the sensor a name and choose its place.";
      } else {
        const { data, response } = await api.POST("/api/sensors", { body });
        shown = data ? { name, key: data.key } : null;
        problem = data
          ? ""
          : response.status === 409
            ? "Another sensor already has that name."
            : "Give the sensor a name and choose its place.";
      }

      if (!problem) {
        reset();
        await load();
      }
    }, setProblem);
  };

  const newKey = (sensor: Schemas["SensorSummary"]) => {
    if (
      !confirm(
        `Give ${sensor.name} a new key? The old one stops working straight away.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      const { data } = await api.POST("/api/sensors/{id}/new-key", {
        params: { path: { id: sensor.id } },
      });
      shown = data ? { name: sensor.name, key: data.key } : null;
    }, setProblem);
  };

  const stop = (sensor: Schemas["SensorSummary"]) => {
    if (
      !confirm(
        `Stop using ${sensor.name}? Its key stops working and its readings are kept.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/sensors/{id}/stop", {
        params: { path: { id: sensor.id } },
      });
      await load();
    }, setProblem);
  };

  const homeAssistant = (key: string) => `rest_command:
  growlog_reading:
    url: ${location.origin}/api/sensor-readings
    method: post
    headers:
      authorization: "Bearer ${key}"
    content_type: application/json
    payload: >-
      {"temperature": {{ states('sensor.greenhouse_temperature') | float }},
       "humidity": {{ states('sensor.greenhouse_humidity') | float }}}

automation:
  - alias: Send greenhouse readings to GrowLog
    trigger:
      - platform: time_pattern
        minutes: "/5"
    action:
      - service: rest_command.growlog_reading`;

  const example = '{"temperature": 18.5, "humidity": 72}';

  const copy = (text: string) =>
    navigator.clipboard?.writeText(text).catch(() => undefined);
</script>

<main>
  <h1>Sensors</h1>
  <p class="muted">
    Temperature and humidity sensors send readings to GrowLog, and Manage,
    Climate charts them for each place. Home Assistant, or any small board that
    can send a web request, can send them.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if shown}
    <div class="card stack" role="status">
      <h2 class="form-heading">The key for {shown.name}</h2>
      <p>
        Copy it now: it won't be shown again. Anyone with it can send readings
        for this sensor, so keep it like a password.
      </p>
      <code class="sensor-key">{shown.key}</code>
      <button class="secondary" onclick={() => copy(shown!.key)}
        >Copy the key</button
      >
      <details>
        <summary>For Home Assistant</summary>
        <p class="muted">
          Put this in configuration.yaml, change the two sensor names to your
          own, and restart Home Assistant. It sends a reading every five
          minutes.
        </p>
        <pre class="sensor-config">{homeAssistant(shown.key)}</pre>
        <button
          class="secondary"
          onclick={() => copy(homeAssistant(shown!.key))}>Copy this</button
        >
      </details>
      <p class="muted">
        Anything else sends a POST to /api/sensor-readings with the header
        "Authorization: Bearer" and the key, and a body such as <code
          >{example}</code
        >. Up to one reading a minute.
      </p>
      <button
        onclick={() => {
          shown = null;
          void attempt(load, setProblem);
        }}>Done</button
      >
    </div>
  {/if}

  <div class="split">
    <section>
      <form class="form-card stack" onsubmit={save}>
        <h2 class="form-heading">
          {editing ? "Change the sensor" : "Add a sensor"}
        </h2>
        <label
          >Name <input
            bind:value={form.name}
            required
            maxlength="100"
            placeholder="Greenhouse 1 sensor"
          /></label
        >
        <label
          >Place <select bind:value={form.locationId} required>
            <option value="">Choose…</option>
            {#each places as place (place.id)}
              <option value={place.id}>{place.label}</option>
            {/each}
          </select></label
        >
        <div class="row">
          <button type="submit">{editing ? "Save" : "Add sensor"}</button>
          {#if editing}
            <button type="button" class="secondary" onclick={reset}
              >Cancel</button
            >
          {/if}
        </div>
      </form>
    </section>

    <section class="stack">
      {#each sensors as sensor (sensor.id)}
        {@const age = sensor.lastReadingAt
          ? readingAge(sensor.lastReadingAt, now)
          : null}
        <div class="card stack tight">
          <strong>{sensor.name}</strong>
          <span>{placeName(sensor.locationId)}</span>
          <span class={age?.stale ? "problem" : "muted"}
            >{age ? `Last heard ${age.words}` : "Not heard from yet"}</span
          >
          <div class="row">
            <button
              class="link-button"
              onclick={() => {
                editing = sensor.id;
                form = { name: sensor.name, locationId: sensor.locationId };
              }}>Change</button
            >
            <button class="link-button" onclick={() => newKey(sensor)}
              >New key</button
            >
            <button class="link-button" onclick={() => stop(sensor)}
              >Stop using</button
            >
          </div>
        </div>
      {:else}
        <p class="muted">No sensors yet.</p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
