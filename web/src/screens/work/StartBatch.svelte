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
  import SowByPicker from "./SowByPicker.svelte";
  import SupplierRegistrations from "./SupplierRegistrations.svelte";
  import { sowByWords } from "../../lib/sowBy";
  import {
    packetShares,
    sowingChoices,
    sowingWords,
    startsCounted,
    type PacketShare,
    type SownMeasure,
  } from "../../lib/sowing";
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
  // The species pick and variety a plant was made for, so a changed variety makes a new plant.
  let plantMadeFor = "";
  // Set while the browser moves back to a step, so a second tap can't move it twice.
  let moving = false;
  let quantity = $state(1);
  let source = $state<Schemas["BatchSource"]>("SeedLot");
  let sourceReference = $state("");
  let supplierId = $state("");
  let newSupplier = $state("");
  let newPassport = $state("");
  let newOther = $state("");
  let sowBy = $state("");
  // Cuttings: the mother plant they're taken from, for plants already on the list.
  let mothers = $state<Schemas["MotherSummary"][]>([]);
  let motherId = $state("");
  let newMother = $state("");

  $effect(() => {
    const id = plantId;
    motherId = "";
    mothers = [];

    if (id) {
      void api
        .GET("/api/mother-plants", { params: { query: { plantId: id } } })
        .then(({ data }) => (mothers = data ?? []))
        .catch(() => (mothers = []));
    }
  });

  const addMother = () =>
    attempt(async () => {
      const { data, response } = await api.POST("/api/mother-plants", {
        body: {
          plantId,
          name: newMother.trim(),
          locationId: null,
          notes: null,
        },
      });

      if (data) {
        mothers =
          (
            await api.GET("/api/mother-plants", {
              params: { query: { plantId } },
            })
          ).data ?? mothers;
        motherId = data.id;
        newMother = "";
      } else {
        problem =
          response.status === 409
            ? "That plant already has a mother with that name."
            : "Please give the mother plant a name.";
      }
    }, setProblem);
  // How the seed was measured; only "I counted them" starts the tray with a number.
  let sownAs = $state<SownMeasure>("Counted");
  let aboutSeeds = $state(200);
  let grams = $state("");
  let packetShare = $state<PacketShare>("Whole");
  let packetSays = $state("");

  const seed = $derived(source === "SeedLot");
  const counting = $derived(!seed || startsCounted(sownAs));
  const sowingReady = $derived(
    counting
      ? quantity >= 1
      : sownAs === "Grams"
        ? Number(grams) > 0
        : sownAs === "About"
          ? aboutSeeds >= 1
          : true,
  );
  const sowing = $derived(
    sowingWords({
      sownAs: seed ? sownAs : null,
      sownAmount: sownAs === "About" ? aboutSeeds : Number(grams),
      packetShare,
      packetSays: packetSays.trim() || null,
    }),
  );
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

    // Only seed can go in uncounted: plants and cuttings are always a number.
    if (chosen !== "SeedLot" && !startsCounted(sownAs)) {
      sownAs = "Counted";
      quantity = Math.max(1, quantity);
    }

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
    moving = false;
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

  const chosenSupplier = $derived(
    suppliers.find((item) => item.id === supplierId),
  );

  async function reloadSuppliers() {
    suppliers = (await api.GET("/api/suppliers")).data ?? [];
  }

  function next() {
    problem = "";
    goTo(stage + 1);
  }

  // Back through the browser's history, so Back after changing something still behaves.
  function change(target: number) {
    if (moving) {
      return;
    }

    moving = true;
    problem = "";
    history.go(target - stage);
    // In case the browser never says it moved.
    setTimeout(() => (moving = false), 1000);
  }

  // The answers on "Where from?" must be complete, even if the browser's Forward skipped past it.
  const whereFromDone = $derived(
    originCountry.trim().length === 2 &&
      (source !== "BoughtIn" || !!supplierId),
  );

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
        name: counting ? "How many" : "Seed sown",
        value: `${counting ? quantity : `${sowing}, not counted yet`}, in a ${containerKinds[containerKind].toLowerCase()}${potSize ? `, ${potSize}` : ""}`,
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

      if (source === "SeedLot" && sowBy) {
        rows.push({ name: "Sow by", value: sowByWords(sowBy), stage: 3 });
      }
    }

    rows.push(
      ...(source === "Cuttings" && motherId
        ? [
            {
              name: "Taken from",
              value:
                mothers.find((item) => item.id === motherId)?.name ??
                "A mother plant",
              stage: 3,
            },
          ]
        : []),
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
          passportRegistrationNumber: newPassport.trim() || null,
          notes: null,
          otherRegistrations: newOther.trim() || null,
        },
      });

      if (data) {
        suppliers = (await api.GET("/api/suppliers")).data ?? [];
        supplierId = data.id;
        newSupplier = "";
        newPassport = "";
        newOther = "";
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
    const madeFor =
      picked?.kind === "species" ? `${picked.id}|${variety.trim()}` : "";

    if (
      !picked ||
      picked.kind === "plant" ||
      (plantId && plantMadeFor === madeFor)
    ) {
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
        plantMadeFor =
          picked?.kind === "species" ? `${picked.id}|${variety.trim()}` : "";
        const outcome = await submit(
          work.startBatch(
            {
              plantId: savedPlantId,
              quantity: counting ? quantity : 0,
              sownAs: seed ? sownAs : null,
              sownAmount: !seed
                ? null
                : sownAs === "About"
                  ? aboutSeeds
                  : sownAs === "Grams"
                    ? Number(grams)
                    : null,
              packetShare: seed && sownAs === "Packet" ? packetShare : null,
              packetSays:
                seed && sownAs === "Packet" ? packetSays.trim() || null : null,
              source,
              sourceReference:
                source === "Cuttings" ? null : optional(sourceReference),
              supplierId: source === "Cuttings" ? null : supplierId || null,
              parentBatchId: null,
              motherPlant: null,
              motherPlantId:
                source === "Cuttings" && motherId ? motherId : null,
              originCountry,
              containerKind,
              locationId: locationId || null,
              potSizeId: potSizeId || null,
              purchaseLineId: prefill?.purchaseLineId ?? null,
              sowBy: source === "SeedLot" && sowBy ? sowBy : null,
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
          `Started ${counting ? `${quantity} ` : ""}${chosenName}${counting ? "" : ", not counted yet"}. Label: ${outcome.labelCode}${failed > 0 ? `. ${failed} photo${failed === 1 ? "" : "s"} didn't save.` : ""}`,
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
    {#if seed}
      <fieldset class="day-picker">
        <legend class="field-label">How much seed went in?</legend>
        <div class="segmented sowing-choices">
          {#each sowingChoices as choice (choice.measure)}
            <button
              aria-pressed={sownAs === choice.measure}
              onclick={() => (sownAs = choice.measure)}>{choice.label}</button
            >
          {/each}
        </div>
      </fieldset>
    {/if}
    {#if counting}
      <Stepper bind:value={quantity} presets={[10, 24, 40, 42, 84]} />
    {:else if sownAs === "About"}
      <p class="muted">
        About how many seeds? A guess, or what the packet says.
      </p>
      <Stepper bind:value={aboutSeeds} presets={[50, 100, 250, 500]} />
    {:else if sownAs === "Grams"}
      <label
        >Grams of seed <input
          bind:value={grams}
          inputmode="decimal"
          placeholder="0.5"
        /></label
      >
    {:else if sownAs === "Packet"}
      <div class="segmented">
        {#each packetShares as item (item.share)}
          <button
            aria-pressed={packetShare === item.share}
            onclick={() => (packetShare = item.share)}>{item.label}</button
          >
        {/each}
      </div>
      <label
        >What does the packet say? (optional) <input
          bind:value={packetSays}
          maxlength="200"
          placeholder="approx. 250 seeds, or 1 g"
        /></label
      >
    {/if}
    {#if !counting}
      <p class="muted">
        The tray shows "not counted yet" until someone counts what's come up:
        when it germinates, or when it's pricked out.
      </p>
    {/if}
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
    <button onclick={next} disabled={!sowingReady}>Next</button>
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
    {#if source === "Cuttings" && plantId}
      <label
        >Taken from <select bind:value={motherId}>
          <option value="">No mother plant recorded</option>
          {#each mothers as mother (mother.id)}
            <option value={mother.id}
              >{mother.name}{mother.locationName
                ? ` · ${mother.locationName}`
                : ""}</option
            >
          {/each}
        </select></label
      >
      {#if !motherId}
        <div class="row">
          <label class="grow"
            >Or a new mother plant <input
              bind:value={newMother}
              maxlength="100"
              placeholder="By the door"
            /></label
          >
          {#if newMother.trim()}
            <button type="button" class="secondary" onclick={addMother}
              >Add</button
            >
          {/if}
        </div>
      {/if}
    {/if}
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
          <label
            >Their plant passport number (if known) <input
              bind:value={newPassport}
              autocapitalize="characters"
              placeholder="GB 123456"
            /></label
          >
          <label
            >Other registration numbers <textarea
              bind:value={newOther}
              rows="2"
              placeholder="Seed standards: 12345"></textarea></label
          >
          <button class="secondary" onclick={addSupplier}>Add supplier</button>
        {/if}
      {:else if chosenSupplier}
        <SupplierRegistrations
          supplier={chosenSupplier}
          onSaved={reloadSuppliers}
        />
      {/if}
      <label>Lot or reference <input bind:value={sourceReference} /></label>
      {#if source === "SeedLot"}
        <SowByPicker bind:value={sowBy} />
      {/if}
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
    <button onclick={next} disabled={!whereFromDone}>Next</button>
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
    {#if !whereFromDone}
      <p class="problem" role="alert">
        "Where from?" isn't finished: bought-in plants need their supplier, and
        every batch its country of origin.
      </p>
    {/if}
    <button
      disabled={busy || !whereFromDone || !picked || !sowingReady}
      onclick={start}>Start batch</button
    >
  {/if}
</ActionFrame>
