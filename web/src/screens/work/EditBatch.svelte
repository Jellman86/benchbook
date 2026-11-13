<script lang="ts">
  import BatchWhere from "./BatchWhere.svelte";
  import { isOn } from "../../lib/features.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { originCountries } from "../../lib/countries";
  import { isoDay, momentOn } from "../../lib/days";
  import { batchChanges, type BatchForm } from "../../lib/batchEdit";
  import { packetShares, sowingChoices } from "../../lib/sowing";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import DayPicker from "../DayPicker.svelte";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchPhotos from "./BatchPhotos.svelte";
  import SowByPicker from "./SowByPicker.svelte";
  import Stepper from "./Stepper.svelte";
  import SupplierRegistrations from "./SupplierRegistrations.svelte";

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
        sowBy: details.sowBy ?? "",
        // Seed recorded before the ways of measuring it existed was counted.
        sownAs:
          details.sownAs ?? (details.source === "SeedLot" ? "Counted" : null),
        sownAmount:
          details.sownAmount == null ? "" : String(details.sownAmount),
        packetShare: details.packetShare ?? null,
        packetSays: details.packetSays ?? "",
      };
      form = { ...before };
    }, setProblem),
  );

  const chosenSupplier = $derived(
    suppliers.find((item) => item.id === form?.supplierId),
  );

  async function reloadSuppliers() {
    suppliers = (await api.GET("/api/suppliers")).data ?? [];
  }

  // Seed sown without a count stays that way: only a count gives it a number.
  const uncounted = $derived(
    !!details &&
      !!details.startedAt &&
      Number(details.quantity) === 0 &&
      !!details.sownAs &&
      details.sownAs !== "Counted",
  );

  // Measured another way than a count, so the tray has no number until it's counted.
  const measuredOtherwise = $derived(
    !!form &&
      form.source === "SeedLot" &&
      !!form.sownAs &&
      form.sownAs !== "Counted",
  );
  // How the seed went in can move between counted and not while nothing has been recorded since.
  const sowingCanChange = $derived(
    !!details &&
      !!form &&
      form.source === "SeedLot" &&
      (uncounted || details.startCanChange),
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

  // Like the server: about so many is a whole number of seeds, grams are more than nothing.
  const sowingValid = $derived(
    !form ||
      !measuredOtherwise ||
      (form.sownAs === "About"
        ? Number.isInteger(Number(form.sownAmount)) &&
          Number(form.sownAmount) >= 1
        : form.sownAs === "Grams"
          ? Number(form.sownAmount) > 0 && Number(form.sownAmount) <= 10_000
          : true),
  );

  const ready = $derived(
    !!form &&
      form.originCountry.trim().length === 2 &&
      (form.source !== "BoughtIn" || !!form.supplierId) &&
      sowingValid,
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
        if (
          form.source === "SeedLot" &&
          form.sownAs === "Counted" &&
          uncounted &&
          form.quantity <= 0
        ) {
          problem = "Say how many seeds went in.";
          return;
        }

        const startChanged =
          !!details.startedAt &&
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
            sowBy: form.source === "SeedLot" && form.sowBy ? form.sowBy : null,
            sownAs: form.source === "SeedLot" ? form.sownAs : null,
            sownAmount:
              form.source === "SeedLot" &&
              (form.sownAs === "About" || form.sownAs === "Grams")
                ? Number(form.sownAmount)
                : null,
            packetShare:
              form.source === "SeedLot" && form.sownAs === "Packet"
                ? (form.packetShare ?? "Part")
                : null,
            packetSays:
              form.source === "SeedLot" && form.sownAs === "Packet"
                ? form.packetSays.trim() || null
                : null,
            originCountry: form.originCountry.trim().toUpperCase(),
            startedAt:
              startChanged && form.startedOn !== before.startedOn
                ? startedAt(form.startedOn)
                : null,
            quantity:
              !measuredOtherwise &&
              startChanged &&
              form.quantity !== before.quantity
                ? form.quantity
                : null,
          },
        });

        if (response.ok) {
          onSaved(`Batch ${details.traceabilityCode} corrected`);
        } else if (
          (error as { problem?: string } | undefined)?.problem ===
          "CountInstead"
        ) {
          checking = false;
          problem =
            form.source === "SeedLot"
              ? "Other work has been recorded on this tray since it was sown, so it can't change between counted and not. Use Count on the tray to give it a number."
              : "Say how many it started with.";
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
    {#if isOn("BatchPhotos")}
      <BatchPhotos {batchId} kinds={["Packet", "Label", "Packaging"]} />
      <p class="muted">Photos are saved as soon as they're taken.</p>
    {/if}

    <BatchWhere {batchId} />

    <label>
      Plant
      <select bind:value={form.plantId} disabled={!!details.motherPlantId}>
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
          disabled={(uncounted ||
            !details.startCanChange ||
            !!details.motherPlantId) &&
            form.source !== item.source}
          onclick={() => (form!.source = item.source)}>{item.label}</button
        >
      {/each}
    </div>

    {#if sowingCanChange}
      <fieldset class="day-picker">
        <legend class="field-label">How much seed went in?</legend>
        <div class="segmented two">
          {#each sowingChoices.filter((choice) => details!.startCanChange || choice.measure !== "Counted") as choice (choice.measure)}
            <button
              aria-pressed={form.sownAs === choice.measure}
              onclick={() => (form!.sownAs = choice.measure)}
              >{choice.label}</button
            >
          {/each}
        </div>
      </fieldset>
      {#if form.sownAs === "About" || form.sownAs === "Grams"}
        <label
          >{form.sownAs === "About" ? "About how many seeds" : "Grams of seed"}
          <input bind:value={form.sownAmount} inputmode="decimal" /></label
        >
      {:else if form.sownAs === "Packet"}
        <div class="segmented">
          {#each packetShares as item (item.share)}
            <button
              aria-pressed={form.packetShare === item.share}
              onclick={() => (form!.packetShare = item.share)}
              >{item.label}</button
            >
          {/each}
        </div>
        <label
          >What does the packet say? (optional) <input
            bind:value={form.packetSays}
            maxlength="200"
          /></label
        >
      {/if}
      {#if measuredOtherwise}
        <p class="muted">
          Sown without a count: the number comes from counting them, when they
          germinate or are pricked out.
        </p>
        {#if !uncounted}
          <p class="caution">
            The tray's {before?.quantity} will go, and it shows as not counted until
            someone counts it.
          </p>
        {/if}
      {/if}
    {/if}

    {#if details.startCanChange && form.startedOn}
      <DayPicker bind:value={form.startedOn} label={whenAsked[form.source]} />
      {#if !measuredOtherwise}
        <p class="field-label">
          {form.source === "SeedLot"
            ? "How many seeds went in"
            : "How many to start with"}
        </p>
        <Stepper bind:value={form.quantity} />
      {/if}
    {:else if details.startedAt && form.startedOn}
      <!-- Work recorded since stays after the start, so it can only move earlier. -->
      <DayPicker
        bind:value={form.startedOn}
        label={whenAsked[form.source]}
        max={before?.startedOn}
      />
      <p class="muted">
        Started with {details.quantity}. Other work has been recorded on these
        plants since, so the day can only move earlier, and how they started and
        the number stay as they are. Use Count to correct the number.
      </p>
    {:else}
      <p class="muted">
        This batch didn't start with a sowing, an arrival or cuttings, so how it
        started, the day and the number can't be changed here.
      </p>
    {/if}

    {#if form.source === "Cuttings"}
      {#if details.motherPlantId}
        <p class="muted">
          Cuttings from {form.motherPlant}. They stay with that mother plant.
        </p>
      {:else}
        <label>Mother plant <input bind:value={form.motherPlant} /></label>
      {/if}
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
      {#if chosenSupplier}
        <SupplierRegistrations
          supplier={chosenSupplier}
          onSaved={reloadSuppliers}
        />
      {/if}
      <label>Lot or reference <input bind:value={form.sourceReference} /></label
      >
      {#if form.source === "SeedLot"}
        <SowByPicker bind:value={form.sowBy} />
      {/if}
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
