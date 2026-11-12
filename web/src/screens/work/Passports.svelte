<script lang="ts">
  import { onMount } from "svelte";
  import DayPicker from "../DayPicker.svelte";
  import { dayLabel } from "../../lib/dates";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
  import IssuePassport from "./IssuePassport.svelte";
  import PassportCard from "./PassportCard.svelte";

  let {
    container,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    onCancel: () => void;
  } = $props();

  type Status = Schemas["ReceivedPassportStatus"];

  const statusLabels: Record<Status, string> = {
    Held: "Held",
    UsedUp: "Used up",
    NoLongerValid: "No longer valid",
  };
  const statuses = Object.keys(statusLabels) as Status[];

  const today = () => new Date().toISOString().slice(0, 10);

  let chosen = $state<string | null>(null);
  let passports = $state<Schemas["PassportResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let adding = $state(false);
  let problem = $state("");
  let form = $state({
    supplierId: "",
    receivedOn: today(),
    partA: "",
    partB: "",
    partC: "",
    partD: "",
    partE: "",
  });

  const setProblem = (message: string) => (problem = message);

  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  async function load(batchId: string) {
    passports =
      (
        await api.GET("/api/received-passports", {
          params: { query: { batchId } },
        })
      ).data ?? [];
  }

  $effect(() => {
    const batchId = line.batchId;
    adding = false;
    void attempt(() => load(batchId), setProblem);
  });

  onMount(() =>
    attempt(async () => {
      suppliers = (await api.GET("/api/suppliers")).data ?? [];
    }, setProblem),
  );

  // Filled in from what's already known, for checking against the label.
  const startAdding = () =>
    attempt(async () => {
      const { data } = await api.GET(
        "/api/batches/{id}/received-passport-draft",
        { params: { path: { id: line.batchId } } },
      );

      form = {
        supplierId: data?.supplierId ?? "",
        receivedOn: today(),
        partA: data?.partA ?? "",
        partB: data?.partB ?? "",
        partC: "",
        partD: data?.partD ?? "",
        partE: "",
      };
      adding = true;
    }, setProblem);

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/received-passports", {
        body: {
          batchId: line.batchId,
          supplierId: form.supplierId,
          receivedOn: form.receivedOn,
          partA: form.partA,
          partB: form.partB,
          partC: optional(form.partC),
          partD: form.partD,
          partE: optional(form.partE),
        },
      });

      if (response.ok) {
        adding = false;
        await load(line.batchId);
      } else {
        problem = "Please check the date and parts A, B and D.";
      }
    }, setProblem);
  };

  const setStatus = (id: string, status: Status) =>
    attempt(async () => {
      await api.POST("/api/received-passports/{id}/status", {
        params: { path: { id } },
        body: { status },
      });
      await load(line.batchId);
    }, setProblem);
</script>

<ActionFrame title="Plant passports" {problem} {onCancel} {container}>
  <BatchChoice lines={container.contents} bind:selected={chosen} />
  <p class="muted">{plantName(line.commonName, line.variety)}</p>

  {#key line.batchId}
    <IssuePassport containerId={container.id} batchId={line.batchId} />
  {/key}

  <h2>Received with the plants</h2>

  {#if adding}
    <form class="stack" onsubmit={save}>
      <p>Copy these exactly as printed on the supplier's label.</p>
      <label>
        Supplier
        <select bind:value={form.supplierId} required>
          <option value="">Choose…</option>
          {#each suppliers as supplier (supplier.id)}
            <option value={supplier.id}>{supplier.name}</option>
          {/each}
        </select>
      </label>
      <DayPicker bind:value={form.receivedOn} label="Date received" />
      <label>A: Botanical name <input bind:value={form.partA} required /></label
      >
      <label
        >B: Registration number <input
          bind:value={form.partB}
          required
        /></label
      >
      <label>C: Traceability code <input bind:value={form.partC} /></label>
      <label>D: Origin <input bind:value={form.partD} required /></label>
      <label>E (if printed) <input bind:value={form.partE} /></label>
      <button type="submit">Save passport</button>
      <button type="button" class="secondary" onclick={() => (adding = false)}
        >Cancel</button
      >
    </form>
  {:else}
    <button class="secondary" onclick={startAdding}
      >Add a received passport</button
    >

    <div class="passport-list">
      {#each passports as passport (passport.id)}
        <div class="stack tight">
          <PassportCard
            caption="Passport received from {passport.supplierName}"
            partA={passport.partA}
            partB={passport.partB}
            partC={passport.partC}
            partD={passport.partD}
            partE={passport.partE}
          >
            <span
              ><strong>{statusLabels[passport.status]}</strong> · from {passport.supplierName},
              {dayLabel(passport.receivedOn)}</span
            >
          </PassportCard>
          {#if passport.plantsChangedSince}
            <p class="caution">
              These plants have been potted on since, so this passport no longer
              covers them. Issue a new one above before selling them where one
              is needed.
            </p>
          {/if}
          <div class="row">
            {#each statuses.filter((status) => status !== passport.status) as status (status)}
              <button
                class="secondary"
                onclick={() => setStatus(passport.id, status)}
                >{statusLabels[status]}</button
              >
            {/each}
          </div>
        </div>
      {:else}
        <p class="muted">No received passports recorded for these plants.</p>
      {/each}
    </div>
  {/if}
</ActionFrame>
