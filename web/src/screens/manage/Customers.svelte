<script lang="ts">
  import { showForm } from "../../lib/showForm";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import FormPanel from "../FormPanel.svelte";

  let { onBack }: { onBack: () => void } = $props();

  type Customer = Schemas["CustomerResponse"];

  const blank = { name: "", address: "", email: "", phone: "", vatNumber: "" };

  let customers = $state<Customer[]>([]);
  let form = $state({ ...blank });
  let formOpen = $state(false);
  let editing = $state<string | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    customers = (await api.GET("/api/customers")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  const body = () => ({
    name: form.name,
    address: optional(form.address),
    email: optional(form.email),
    phone: optional(form.phone),
    vatNumber: optional(form.vatNumber),
  });

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = editing
        ? await api.PUT("/api/customers/{id}", {
            params: { path: { id: editing } },
            body: body(),
          })
        : await api.POST("/api/customers", { body: body() });

      if (response.ok) {
        form = { ...blank };
        editing = null;
        problem = "";
        await load();
      } else {
        problem =
          response.status === 409
            ? "There's already a customer with that name."
            : "Please give the customer a name.";
      }
    }, setProblem);
  };

  function edit(customer: Customer) {
    formOpen = true;
    void showForm();
    editing = customer.id;
    form = {
      name: customer.name,
      address: customer.address ?? "",
      email: customer.email ?? "",
      phone: customer.phone ?? "",
      vatNumber: customer.vatNumber ?? "",
    };
  }

  const forget = (customer: Customer) => {
    if (
      !confirm(
        `Forget ${customer.name}'s address, email and phone? Invoices already issued keep what they were issued with, as the law requires.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/customers/{id}/forget", {
        params: { path: { id: customer.id } },
      });
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Customers</h1>

  <div class="split">
    <section>
      <FormPanel
        bind:open={formOpen}
        label={editing ? "Change customer" : "Add a customer"}
      >
        <form class="stack" onsubmit={save} data-edit-form>
          <label>Name <input bind:value={form.name} required /></label>
          <label
            >Address <textarea bind:value={form.address} rows="3"
            ></textarea></label
          >
          <div class="field-grid">
            <label>Email <input type="email" bind:value={form.email} /></label>
            <label
              >Phone <input inputmode="tel" bind:value={form.phone} /></label
            >
          </div>
          <label>VAT number <input bind:value={form.vatNumber} /></label>
          <div class="form-actions">
            <button type="submit">{editing ? "Save changes" : "Add"}</button>
            {#if editing}
              <button
                type="button"
                class="secondary"
                onclick={() => {
                  editing = null;
                  form = { ...blank };
                }}>Cancel</button
              >
            {/if}
          </div>
        </form>
      </FormPanel>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      {#each customers as customer (customer.id)}
        <div class="card">
          <strong>{customer.name}</strong>
          {#if customer.address}<span class="muted plant-note"
              >{customer.address}</span
            >{/if}
          <span class="muted"
            >{[
              customer.email,
              customer.phone,
              customer.vatNumber ? `VAT ${customer.vatNumber}` : null,
            ]
              .filter(Boolean)
              .join(" · ")}</span
          >
          <div class="row">
            <button class="secondary" onclick={() => edit(customer)}
              >Change</button
            >
            <button class="secondary" onclick={() => forget(customer)}
              >Forget contact details</button
            >
          </div>
        </div>
      {:else}
        <p class="note-empty muted">
          There are no customers yet. Add the businesses you invoice.
        </p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
