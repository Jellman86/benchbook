<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import { onDestroy, onMount } from "svelte";
  import {
    focusHeading,
    onStepChange,
    pushStep,
    stepOf,
  } from "../../lib/steps";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import {
    outcomeProblem,
    plantName,
    submit,
    work,
    workPlaces,
    type BatchPrefill,
    type PickedPlant,
  } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import PlantSearch from "./PlantSearch.svelte";
  import PlantThumb from "./PlantThumb.svelte";
  import Stepper from "./Stepper.svelte";
  import DayPicker from "../DayPicker.svelte";
  import { isoDay, momentOn, onDay } from "../../lib/days";
  import { originCountries } from "../../lib/countries";

  type PhotoKind = Schemas["BatchPhotoKind"] | "Plant";

  let {
    prefill,
    onDone,
    onCancel,
  }: {
    prefill?: BatchPrefill;
    onDone: (message: string, eventId: string, newLabel?: string) => void;
    onCancel: () => void;
  } = $props();

  const stages = [
    "Find the plant",
    "How many?",
    "Where from?",
    "Photos",
    "Where is it going?",
    "Check and start",
  ];

  const containerKinds: Record<Schemas["ContainerKind"], string> = {
    Tray: "Tray",
    PotGroup: "Group of pots",
    BenchLot: "Bench lot",
  };

  const photoChoices: { kind: PhotoKind; label: string }[] = [
    { kind: "Packet", label: "Seed packet" },
    { kind: "Label", label: "Label" },
    { kind: "Packaging", label: "Packaging" },
    { kind: "Plant", label: "The plant itself" },
  ];

  const sources: { source: Schemas["BatchSource"]; label: string }[] = [
    { source: "SeedLot", label: "Seed" },
    { source: "BoughtIn", label: "Bought in" },
    { source: "Cuttings", label: "Cuttings" },
  ];

  let plants = $state<Schemas["PlantResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);

  let stage = $state(1);
  let picked = $state<PickedPlant | null>(null);
  let details = $state<Schemas["SpeciesDetailsResponse"] | null>(null);
  let variety = $state("");
  let plantId = $state("");
  let quantity = $state(1);
  let source = $state<Schemas["BatchSource"]>("SeedLot");
  let sourceReference = $state("");
  let supplierId = $state("");
  let newSupplier = $state("");
  let locationId = $state("");
  let potSizeId = $state("");
  let containerKind = $state<Schemas["ContainerKind"]>("Tray");
  // Grown here starts as Great Britain; bought-in plants must have their origin chosen.
  let originCountry = $state("GB");
  let otherCountry = $state(false);
  // Sown, arrived or taken today unless someone says otherwise: work is often written up later.
  let startedOn = $state(isoDay(new Date()));

  const whenAsked: Record<Schemas["BatchSource"], string> = {
    SeedLot: "When were they sown?",
    BoughtIn: "When did they arrive?",
    Cuttings: "When were the cuttings taken?",
  };
  const started: Record<Schemas["BatchSource"], string> = {
    SeedLot: "Sown",
    BoughtIn: "Arrived",
    Cuttings: "Cuttings taken",
  };

  function chooseSource(chosen: Schemas["BatchSource"]) {
    source = chosen;

    if (chosen === "BoughtIn") {
      originCountry = "";
      // Suppliers come most recently used first.
      supplierId = supplierId || (suppliers[0]?.id ?? "");
    } else if (!originCountry) {
      originCountry = "GB";
    }
  }
  let photos = $state<Partial<Record<PhotoKind, { blob: Blob; url: string }>>>(
    {},
  );
  let problem = $state("");
  let busy = $state(false);

  const setProblem = (message: string) => (problem = message);

  const confirming = $derived(stage === 1 && picked?.kind === "species");

  const chosenName = $derived.by(() => {
    if (picked?.kind === "species") {
      return plantName(
        picked.commonName ?? picked.scientificName,
        variety.trim(),
      );
    }

    return picked ? plantName(picked.commonName, picked.variety) : "";
  });

  onMount(() =>
    attempt(async () => {
      plants = (await api.GET("/api/plants")).data ?? [];
      suppliers = (await api.GET("/api/suppliers")).data ?? [];
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
      locationId = places[0]?.id ?? "";
      potSizeId = potSizes[0]?.id ?? "";

      if (prefill) {
        source = "BoughtIn";
        originCountry = "";
        supplierId = prefill.supplierId;
        startedOn =
          prefill.receivedOn && prefill.receivedOn <= startedOn
            ? prefill.receivedOn
            : startedOn;
        quantity = prefill.quantity;
      }
    }, setProblem),
  );

  onMount(() => onStepChange(stepped));

  onDestroy(() => {
    for (const photo of Object.values(photos)) {
      URL.revokeObjectURL(photo.url);
    }
  });

  function pick(choice: PickedPlant) {
    problem = "";
    picked = choice;
    details = null;
    plantId = choice.kind === "plant" ? choice.id : "";

    if (choice.kind === "species") {
      void attempt(async () => {
        const { data } = await api.GET("/api/species/{id}/details", {
          params: { path: { id: choice.id } },
        });

        details = data ?? null;
      }, setProblem);
    }

    if (choice.kind !== "species") {
      goTo(2);
    }
  }

  // Each step is a step in the browser's history, so the phone's Back button goes back a step,
  // keeping what was entered, rather than leaving New batch.
  function goTo(next: number) {
    stage = next;
    pushStep("batchStage", next);
    void focusHeading();
  }

  function stepped() {
    const kept = stepOf<number>("batchStage") ?? 1;

    if (kept < stage) {
      stage = kept;

      if (stage === 1 && picked?.kind !== "species") {
        picked = null;
      }
    } else {
      stage = kept;
    }

    void focusHeading();
  }

  function back() {
    problem = "";

    if (stage > 1) {
      history.back();
    } else if (picked) {
      picked = null;
    } else {
      onCancel();
    }
  }

  function next() {
    problem = "";
    goTo(stage + 1);
  }

  // Back through the browser's history, so Back after changing something still behaves.
  function change(target: number) {
    problem = "";
    history.go(target - stage);
  }

  /** Everything about to be recorded, each with the step that changes it. */
  const summary = $derived.by(() => {
    const supplier = suppliers.find((item) => item.id === supplierId)?.name;
    const country =
      originCountries.find((item) => item.code === originCountry)?.name ??
      originCountry;
    const potSize = potSizes.find((item) => item.id === potSizeId)?.name;
    const photoCount = Object.keys(photos).length;
    const rows: { name: string; value: string; stage: number }[] = [
      { name: "Plant", value: chosenName, stage: 1 },
      {
        name: "How many",
        value: `${quantity}, in a ${containerKinds[containerKind].toLowerCase()}${potSize ? `, ${potSize}` : ""}`,
        stage: 2,
      },
      {
        name: started[source],
        value: onDay(startedOn).replace(/^on /, ""),
        stage: 3,
      },
    ];

    if (source !== "Cuttings") {
      rows.push({
        name: "Supplier",
        value: supplier ?? "Not listed",
        stage: 3,
      });
      if (sourceReference.trim()) {
        rows.push({
          name: "Lot or reference",
          value: sourceReference.trim(),
          stage: 3,
        });
      }
    }

    rows.push(
      { name: "Country of origin", value: country, stage: 3 },
      {
        name: "Photos",
        value: photoCount === 0 ? "None" : String(photoCount),
        stage: 4,
      },
      {
        name: "Place",
        value:
          places.find((item) => item.id === locationId)?.label ?? "Not set",
        stage: 5,
      },
    );

    return rows;
  });

  const addSupplier = () =>
    attempt(async () => {
      const name = newSupplier.trim();
      const { data, response } = await api.POST("/api/suppliers", {
        body: {
          name,
          contactName: null,
          phone: null,
          email: null,
          address: null,
          accountReference: null,
          supplies: null,
          typicalLeadTimeDays: null,
          passportRegistrationNumber: null,
          notes: null,
        },
      });

      if (data) {
        suppliers = (await api.GET("/api/suppliers")).data ?? [];
        supplierId = data.id;
        newSupplier = "";
        problem = "";
      } else {
        problem =
          response.status === 409
            ? "That supplier is already on the list."
            : "Please enter a name.";
      }
    }, setProblem);

  async function choosePhoto(event: Event, kind: PhotoKind) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    const blob = await shrinkPhoto(file);
    const previous = photos[kind];

    if (previous) {
      URL.revokeObjectURL(previous.url);
    }

    photos[kind] = { blob, url: URL.createObjectURL(blob) };
    input.value = "";
  }

  /** Adds the plant only now, so abandoning the steps leaves nothing behind. */
  async function resolvePlant(): Promise<string | null> {
    if (plantId || !picked || picked.kind === "plant") {
      return plantId;
    }

    if (picked.kind === "species") {
      const { data, response } = await api.POST("/api/plants/from-species", {
        body: {
          speciesId: picked.id,
          variety: optional(variety),
          category: null,
          position: details?.suggestedPosition ?? null,
          watering: details?.suggestedWatering ?? null,
          hardiness: details?.suggestedHardiness ?? null,
          xylellaHost: null,
          protectedVariety: false,
          prices: [],
        },
      });

      if (!data) {
        problem =
          response.status === 409
            ? "That plant was taken off the list. Ask the owner to restore it."
            : "That plant couldn't be added. Please try again.";
      }

      return data?.id ?? null;
    }

    const { data, response } = await api.POST("/api/plants", {
      body: {
        commonName: picked.commonName,
        variety: picked.variety,
        botanicalName: picked.botanicalName,
        category: null,
        position: null,
        watering: null,
        hardiness: null,
        xylellaHost: null,
        protectedVariety: false,
        prices: [],
      },
    });

    if (!data) {
      problem =
        response.status === 409
          ? "That plant is already on the list. Go back and search for it."
          : "Please check the name.";
    }

    return data?.id ?? null;
  }

  async function sendPhotos(batchId: string, savedPlantId: string) {
    let failed = 0;

    for (const [kind, photo] of Object.entries(photos)) {
      const address =
        kind === "Plant"
          ? `/api/plants/${savedPlantId}/photo`
          : `/api/batches/${batchId}/photos?kind=${kind}`;

      try {
        const response = await fetch(address, {
          method: kind === "Plant" ? "PUT" : "POST",
          body: photo.blob,
        });
        failed += response.ok ? 0 : 1;
      } catch {
        failed += 1;
      }
    }

    return failed;
  }

  const start = () =>
    attempt(async () => {
      busy = true;

      try {
        const savedPlantId = await resolvePlant();

        if (!savedPlantId) {
          return;
        }

        plantId = savedPlantId;
        const outcome = await submit(
          work.startBatch(
            {
              plantId: savedPlantId,
              quantity,
              source,
              sourceReference: optional(sourceReference),
              supplierId: source === "Cuttings" ? null : supplierId || null,
              parentBatchId: null,
              motherPlant: null,
              originCountry,
              containerKind,
              locationId: locationId || null,
              potSizeId: potSizeId || null,
              purchaseLineId: prefill?.purchaseLineId ?? null,
            },
            momentOn(startedOn),
          ),
        );

        if (outcome.kind !== "stored") {
          problem = outcomeProblem(outcome);
          return;
        }

        const failed = outcome.batchId
          ? await sendPhotos(outcome.batchId, savedPlantId)
          : Object.keys(photos).length;

        onDone(
          `Started ${quantity} ${chosenName}. Label: ${outcome.labelCode}${failed > 0 ? `. ${failed} photo${failed === 1 ? "" : "s"} didn't save.` : ""}`,
          outcome.eventId,
          outcome.labelCode,
        );
      } finally {
        busy = false;
      }
    }, setProblem);
</script>

<ActionFrame title={stages[stage - 1]} {problem} onCancel={back}>
  <p class="muted">Step {stage} of {stages.length}</p>
  <div class="stage-bar" aria-hidden="true">
    {#each stages as name, index (name)}
      <span class:reached={index < stage}></span>
    {/each}
  </div>
  {#if prefill}<p class="muted">From purchase: {prefill.description}</p>{/if}

  {#if stage === 1 && !confirming}
    <PlantSearch recent={plants} onPick={pick} />
  {:else if stage === 1 && picked?.kind === "species"}
    <div class="card">
      <div class="plant-head">
        <PlantThumb url={picked.photoUrl} />
        <span class="plant-names">
          <strong>{picked.commonName ?? picked.scientificName}</strong>
          {#if picked.commonName}<em>{picked.scientificName}</em>{/if}
        </span>
      </div>
      {#if picked.credit}<p class="muted credit">{picked.credit}</p>{/if}
    </div>
    {#if details?.suggestedPosition || details?.suggestedWatering || details?.suggestedHardiness}
      <p class="muted">
        From Wikipedia, and saved with the plant:
        {[
          details.suggestedPosition,
          details.suggestedWatering,
          details.suggestedHardiness,
        ]
          .filter(Boolean)
          .join(". ")}. You can change these under Plants.
      </p>
    {/if}
    <label>Variety (if there is one) <input bind:value={variety} /></label>
    <button onclick={next}>Next</button>
    <button class="secondary" onclick={() => (picked = null)}
      >Choose a different plant</button
    >
  {:else if stage === 2}
    <p class="muted">{chosenName}</p>
    <Stepper bind:value={quantity} presets={[10, 24, 40, 42, 84]} />
    <label>
      Going into
      <select bind:value={containerKind}>
        <option value="Tray">Tray</option>
        <option value="PotGroup">Group of pots</option>
        <option value="BenchLot">Bench lot</option>
      </select>
    </label>
    <label>
      Pot size
      <select bind:value={potSizeId}>
        <option value="">Not set</option>
        {#each potSizes as size (size.id)}
          <option value={size.id}>{size.name}</option>
        {/each}
      </select>
    </label>
    <button onclick={next}>Next</button>
  {:else if stage === 3}
    <div class="segmented">
      {#each sources as item (item.source)}
        <button
          class={source === item.source ? "" : "secondary"}
          aria-pressed={source === item.source}
          onclick={() => chooseSource(item.source)}>{item.label}</button
        >
      {/each}
    </div>
    <DayPicker bind:value={startedOn} label={whenAsked[source]} />
    {#if source !== "Cuttings"}
      <label>
        Supplier
        <select bind:value={supplierId}>
          <option value="">Not listed</option>
          {#each suppliers as supplier (supplier.id)}
            <option value={supplier.id}>{supplier.name}</option>
          {/each}
        </select>
      </label>
      {#if !supplierId}
        <label>New supplier <input bind:value={newSupplier} /></label>
        {#if newSupplier.trim()}
          <button class="secondary" onclick={addSupplier}>Add supplier</button>
        {/if}
      {/if}
      <label>Lot or reference <input bind:value={sourceReference} /></label>
    {/if}
    <label>
      Country of origin
      <select
        value={originCountries.some((item) => item.code === originCountry)
          ? originCountry
          : originCountry
            ? "other"
            : ""}
        onchange={(event) => {
          const chosen = event.currentTarget.value;
          originCountry = chosen === "other" ? "" : chosen;
          otherCountry = chosen === "other";
        }}
      >
        <option value="">Choose…</option>
        {#each originCountries as item (item.code)}
          <option value={item.code}>{item.name}</option>
        {/each}
        <option value="other">Somewhere else…</option>
      </select>
    </label>
    {#if otherCountry}
      <label
        >Two-letter country code, as on the passport <input
          bind:value={originCountry}
          maxlength="2"
          autocapitalize="characters"
        /></label
      >
    {/if}
    {#if source === "BoughtIn" && !supplierId}
      <p class="muted">
        Bought-in plants need their supplier, for the passport record.
      </p>
    {/if}
    <button
      onclick={next}
      disabled={originCountry.trim().length !== 2 ||
        (source === "BoughtIn" && !supplierId)}>Next</button
    >
  {:else if stage === 4}
    <p class="muted">Take a photo of anything worth keeping. All optional.</p>
    <div class="card-grid">
      {#each photoChoices as choice (choice.kind)}
        <label class="photo-choice">
          {#if photos[choice.kind]}
            <img class="plant-thumb" src={photos[choice.kind]?.url} alt="" />
          {:else}
            <span class="plant-thumb placeholder"><Camera size={26} /></span>
          {/if}
          <span>{choice.label}</span>
          <input
            type="file"
            accept="image/*"
            onchange={(event) => choosePhoto(event, choice.kind)}
          />
        </label>
      {/each}
    </div>
    <button onclick={next}
      >{Object.keys(photos).length > 0 ? "Next" : "Skip photos"}</button
    >
  {:else if stage === 5}
    <label>
      Place
      <select bind:value={locationId}>
        <option value="">Not set</option>
        {#each places as place (place.id)}
          <option value={place.id}>{place.label}</option>
        {/each}
      </select>
    </label>
    <button onclick={next}>Next</button>
  {:else}
    <p class="muted">
      Check these before starting: the label prints with them, and the passport
      record relies on them.
    </p>
    <dl class="facts card batch-check">
      {#each summary as row (row.name)}
        <dt>{row.name}</dt>
        <dd>
          <span>{row.value}</span>
          <button class="link-button" onclick={() => change(row.stage)}
            >Change<span class="visually-hidden">
              {row.name.toLowerCase()}</span
            ></button
          >
        </dd>
      {/each}
    </dl>
    <button disabled={busy} onclick={start}>Start batch</button>
  {/if}
</ActionFrame>
