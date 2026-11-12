<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { originCountries } from "../../lib/countries";
  import { isoDay, momentOn, onDay } from "../../lib/days";
  import { batchChanges, type BatchForm } from "../../lib/batchEdit";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import DayPicker from "../DayPicker.svelte";
  import ActionFrame from "./ActionFrame.svelte";
  import Stepper from "./Stepper.svelte";

  // Correcting a batch after it started. Every correction is kept, with who made it.
  let {
    batchId,
    onSaved,
    onCancel,
  }: {
    batchId: string;
    onSaved: (message: string) => void;
    onCancel: () => void;
  } = $props();

  const sources: { source: Schemas["BatchSource"]; label: string }[] = [
    { source: "SeedLot", label: "Seed" },
    { source: "BoughtIn", label: "Bought in" },
    { source: "Cuttings", label: "Cuttings" },
  ];
  const whenAsked: Record<Schemas["BatchSource"], string> = {
    SeedLot: "When were they sown?",
    BoughtIn: "When did they arrive?",
    Cuttings: "When were the cuttings taken?",
  };

  let details = $state<Schemas["BatchDetails"] | null>(null);
  let plants = $state<Schemas["PlantResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let form = $state<BatchForm | null>(null);
  let before = $state<BatchForm | null>(null);
  let checking = $state(false);
  let problem = $state("");
  let busy = $state(false);

  const setProblem = (message: string) => (problem = message);

  onMount(() =>
    attempt(async () => {
      const [loaded, plantList, supplierList] = await Promise.all([
        api.GET("/api/batches/{id}", { params: { path: { id: batchId } } }),
        api.GET("/api/plants"),
        api.GET("/api/suppliers"),
      ]);

      // Plants no longer used stay out of the list, unless this batch is one of them.
      plants = (plantList.data ?? []).filter(
        (plant) => !plant.archived || plant.id === loaded.data?.plantId,
      );
      suppliers = supplierList.data ?? [];
      details = loaded.data ?? null;

      if (!details) {
        problem = "That batch couldn't be found.";
        return;
      }

      before = {
        plantId: details.plantId,
        source: details.source,
        sourceReference: details.sourceReference ?? "",
        supplierId: details.supplierId ?? "",
        motherPlant: details.motherPlant ?? "",
        originCountry: details.originCountry,
        startedOn: details.startedAt ? isoDay(new Date(details.startedAt)) : "",
        quantity: Number(details.quantity ?? 0),
      };
      form = { ...before };
    }, setProblem),
  );

  const changes = $derived(
    form && before ? batchChanges(before, form, plants, suppliers) : [],
  );
  const otherCountry = $derived(
    !!form &&
      !originCountries.some((item) => item.code === form!.originCountry),
  );
  // Received passports and split batches keep their own details, so say so when the details they share change.
  const keptElsewhere = $derived.by(() => {
    if (!details) {
      return "";
    }

    const shared = ["Plant", "Source", "Supplier", "Country of origin"];

    if (!changes.some((change) => shared.includes(change.name))) {
      return "";
    }

    const passports = Number(details.receivedPassports);
    const splits = Number(details.splitInto);

    return [
      passports > 0
        ? `${passports === 1 ? "Its received passport keeps" : `Its ${passports} received passports keep`} what was printed on ${passports === 1 ? "it" : "them"}; add a new one if the plants came with another.`
        : "",
      splits > 0
        ? `${splits === 1 ? "The batch split from it keeps" : `The ${splits} batches split from it keep`} their own details.`
        : "",
    ]
      .filter(Boolean)
      .join(" ");
  });

  const ready = $derived(
    !!form &&
      form.originCountry.trim().length === 2 &&
      (form.source !== "BoughtIn" || !!form.supplierId),
  );

  /** The new start keeps its time of day, but never lands in the future. */
  function startedAt(day: string): string {
    const moment = momentOn(day, new Date(details!.startedAt!));
    return new Date(moment) > new Date() ? new Date().toISOString() : moment;
  }

  const save = () =>
    attempt(async () => {
      if (!form || !before || !details) {
        return;
      }

      busy = true;

      try {
        const startChanged =
          details.startCanChange &&
          (form.startedOn !== before.startedOn ||
            form.quantity !== before.quantity);
        const cuttings = form.source === "Cuttings";
        const { response, error } = await api.PUT("/api/batches/{id}", {
          params: { path: { id: batchId } },
          body: {
            plantId: form.plantId,
            source: form.source,
            sourceReference: cuttings ? null : optional(form.sourceReference),
            supplierId: cuttings ? null : form.supplierId || null,
            motherPlant: cuttings ? optional(form.motherPlant) : null,
            originCountry: form.originCountry.trim().toUpperCase(),
            startedAt:
              startChanged && form.startedOn !== before.startedOn
                ? startedAt(form.startedOn)
                : null,
            quantity:
              startChanged && form.quantity !== before.quantity
                ? form.quantity
                : null,
          },
        });

        if (response.ok) {
          onSaved(`Batch ${details.traceabilityCode} corrected`);
        } else if (
          (error as { problem?: string } | undefined)?.problem === "StartLocked"
        ) {
          // Work was recorded meanwhile: the start stays as it was, and the form says so.
          checking = false;
          details.startCanChange = false;
          form.startedOn = before.startedOn;
          form.quantity = before.quantity;
          form.source = before.source;
          problem =
            "Other work has been recorded on these plants since, so how they started, the day and the number can't be changed. Use Count to correct the number.";
        } else {
          checking = false;
          problem =
            response.status === 400
              ? "That couldn't be saved. Bought-in plants need their supplier, and the country is a two-letter code."
              : response.status === 404
                ? "That batch no longer exists."
                : "That couldn't be saved. Please try again.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);
</script>

<ActionFrame
  title={checking
    ? "Check the changes"
    : `Edit batch ${details?.traceabilityCode ?? ""}`}
  {problem}
  onCancel={checking ? () => (checking = false) : onCancel}
>
  {#if !form || !details}
    <p class="muted">Loading…</p>
  {:else if checking}
    {#if changes.length === 0}
      <p class="muted">Nothing has changed.</p>
      <button class="secondary" onclick={() => (checking = false)}
        >Back to the batch</button
      >
    {:else}
      <p class="muted">
        These corrections are kept with your name, and show on the batch's trace
        report.
      </p>
      <dl class="facts card batch-check">
        {#each changes as change (change.name)}
          <dt>{change.name}</dt>
          <dd>
            <span
              ><s>{change.before || "Not set"}</s> → {change.after ||
                "Not set"}</span
            >
          </dd>
        {/each}
      </dl>
      {#if keptElsewhere}<p class="caution">{keptElsewhere}</p>{/if}
      <button disabled={busy} onclick={save}>Save changes</button>
      <button class="secondary" onclick={() => (checking = false)}
        >Change something else</button
      >
    {/if}
  {:else}
    <label>
      Plant
      <select bind:value={form.plantId}>
        {#each plants as plant (plant.id)}
          <option value={plant.id}
            >{plant.commonName}{plant.variety
              ? ` '${plant.variety}'`
              : ""}</option
          >
        {/each}
      </select>
    </label>

    <div class="segmented">
      {#each sources as item (item.source)}
        <button
          aria-pressed={form.source === item.source}
          disabled={!details.startCanChange && form.source !== item.source}
          onclick={() => (form!.source = item.source)}>{item.label}</button
        >
      {/each}
    </div>

    {#if details.startCanChange && form.startedOn}
      <DayPicker bind:value={form.startedOn} label={whenAsked[form.source]} />
      <p class="field-label">How many to start with</p>
      <Stepper bind:value={form.quantity} />
    {:else}
      <p class="muted">
        {#if details.startedAt}
          Started {onDay(isoDay(new Date(details.startedAt))).replace(
            /^on /,
            "",
          )}
          with {details.quantity}. Other work has been recorded on these plants
          since, so how they started, the day and the number can't be changed
          here. Use Count to correct the number.
        {:else}
          This batch didn't start with a sowing, an arrival or cuttings, so how
          it started, the day and the number can't be changed here.
        {/if}
      </p>
    {/if}

    {#if form.source === "Cuttings"}
      <label>Mother plant <input bind:value={form.motherPlant} /></label>
    {:else}
      <label>
        Supplier
        <select bind:value={form.supplierId}>
          <option value="">Not listed</option>
          {#each suppliers as supplier (supplier.id)}
            <option value={supplier.id}>{supplier.name}</option>
          {/each}
        </select>
      </label>
      <label>Lot or reference <input bind:value={form.sourceReference} /></label
      >
    {/if}

    <label>
      Country of origin
      <select
        value={otherCountry ? "other" : form.originCountry}
        onchange={(event) => {
          const chosen = event.currentTarget.value;
          form!.originCountry = chosen === "other" ? "" : chosen;
        }}
      >
        {#each originCountries as item (item.code)}
          <option value={item.code}>{item.name}</option>
        {/each}
        <option value="other">Somewhere else…</option>
      </select>
    </label>
    {#if otherCountry}
      <label
        >Two-letter country code, as on the passport <input
          bind:value={form.originCountry}
          maxlength="2"
          autocapitalize="characters"
        /></label
      >
    {/if}
    {#if form.source === "BoughtIn" && !form.supplierId}
      <p class="muted">
        Bought-in plants need their supplier, for the passport record.
      </p>
    {/if}

    <button disabled={!ready} onclick={() => (checking = true)}
      >Check the changes</button
    >
  {/if}
</ActionFrame>
