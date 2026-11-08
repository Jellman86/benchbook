<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  type Supplier = Schemas["SupplierResponse"];

  const empty = {
    name: "",
    contactName: "",
    phone: "",
    email: "",
    address: "",
    accountReference: "",
    supplies: "",
    typicalLeadTimeDays: "",
    passportRegistrationNumber: "",
    notes: "",
  };

  let suppliers = $state<Supplier[]>([]);
  let form = $state({ ...empty });
  let editing = $state<string | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    suppliers = (await api.GET("/api/suppliers")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  function reset() {
    form = { ...empty };
    editing = null;
  }

  function edit(supplier: Supplier) {
    editing = supplier.id;
    form = {
      name: supplier.name,
      contactName: supplier.contactName ?? "",
      phone: supplier.phone ?? "",
      email: supplier.email ?? "",
      address: supplier.address ?? "",
      accountReference: supplier.accountReference ?? "",
      supplies: supplier.supplies ?? "",
      typicalLeadTimeDays: supplier.typicalLeadTimeDays?.toString() ?? "",
      passportRegistrationNumber: supplier.passportRegistrationNumber ?? "",
      notes: supplier.notes ?? "",
    };
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    const leadTime = optional(form.typicalLeadTimeDays);
    const body = {
      name: form.name,
      contactName: optional(form.contactName),
      phone: optional(form.phone),
      email: optional(form.email),
      address: optional(form.address),
      accountReference: optional(form.accountReference),
      supplies: optional(form.supplies),
      typicalLeadTimeDays: leadTime === null ? null : Number(leadTime),
      passportRegistrationNumber: optional(form.passportRegistrationNumber),
      notes: optional(form.notes),
    };

    return attempt(async () => {
      const { response } = editing
        ? await api.PUT("/api/suppliers/{id}", {
            params: { path: { id: editing } },
            body,
          })
        : await api.POST("/api/suppliers", { body });

      if (response.ok) {
        reset();
        await load();
      } else {
        problem =
          response.status === 409
            ? "That supplier is already on the list."
            : "Please enter a name.";
      }
    }, setProblem);
  };

  const archive = (id: string) =>
    attempt(async () => {
      await api.POST("/api/suppliers/{id}/archive", {
        params: { path: { id } },
      });
      await load();
    }, setProblem);
</script>

<main>
  <h1>Suppliers</h1>

  <div class="split">
    <section>
      <form class="stack" onsubmit={save}>
        <label>Name <input bind:value={form.name} required /></label>
        <label>Contact name <input bind:value={form.contactName} /></label>
        <label>Phone <input bind:value={form.phone} inputmode="tel" /></label>
        <label>Email <input bind:value={form.email} inputmode="email" /></label>
        <label>Address <input bind:value={form.address} /></label>
        <label
          >Account reference <input bind:value={form.accountReference} /></label
        >
        <label>What they supply <input bind:value={form.supplies} /></label>
        <label
          >Typical lead time (days)
          <input bind:value={form.typicalLeadTimeDays} inputmode="numeric" />
        </label>
        <label
          >Passport registration number
          <input bind:value={form.passportRegistrationNumber} />
        </label>
        <label>Notes <input bind:value={form.notes} /></label>
        <button type="submit">{editing ? "Save changes" : "Add"}</button>
        {#if editing}
          <button type="button" class="secondary" onclick={reset}>Cancel</button
          >
        {/if}
      </form>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#each suppliers as supplier (supplier.id)}
        <div class="card">
          <strong>{supplier.name}</strong>
          <div class="row">
            <button class="secondary" onclick={() => edit(supplier)}
              >Change</button
            >
            <button class="secondary" onclick={() => archive(supplier.id)}
              >No longer used</button
            >
          </div>
        </div>
      {/each}
    </section>
  </div>

  <button class="secondary mobile-only" onclick={onBack}>Back</button>
</main>
