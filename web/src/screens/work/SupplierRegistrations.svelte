<script lang="ts">
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";

  // A supplier's registration numbers, shown beside the supplier and changeable by anyone recording work.
  let {
    supplier,
    onSaved,
  }: {
    supplier: Schemas["SupplierResponse"];
    onSaved: () => Promise<void> | void;
  } = $props();

  let editing = $state(false);
  let passport = $state("");
  let other = $state("");
  let problem = $state("");
  let busy = $state(false);

  function start() {
    passport = supplier.passportRegistrationNumber ?? "";
    other = supplier.otherRegistrations ?? "";
    problem = "";
    editing = true;
  }

  async function save() {
    busy = true;

    try {
      const { response } = await api.PUT("/api/suppliers/{id}/registrations", {
        params: { path: { id: supplier.id } },
        body: {
          passportRegistrationNumber: passport.trim() || null,
          otherRegistrations: other.trim() || null,
        },
      });

      if (response.ok) {
        editing = false;
        await onSaved();
      } else {
        problem = "Those numbers couldn't be saved. Please make them shorter.";
      }
    } catch {
      problem = "Those numbers couldn't be saved. Please check the connection.";
    } finally {
      busy = false;
    }
  }
</script>

<div class="supplier-registrations stack tight">
  {#if editing}
    <label
      >{supplier.name}'s plant passport number
      <input
        bind:value={passport}
        autocapitalize="characters"
        placeholder="GB 123456"
      /></label
    >
    <label
      >Other registration numbers <textarea
        bind:value={other}
        rows="2"
        placeholder="Seed standards: 12345"></textarea></label
    >
    <p class="muted">They're saved for {supplier.name} straight away.</p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <div class="row">
      <button type="button" disabled={busy} onclick={save}>Save numbers</button>
      <button type="button" class="secondary" onclick={() => (editing = false)}
        >Cancel</button
      >
    </div>
  {:else}
    <p class="muted">
      {#if supplier.passportRegistrationNumber || supplier.otherRegistrations}
        {#if supplier.passportRegistrationNumber}Passport number {supplier.passportRegistrationNumber}.{/if}
        {#if supplier.otherRegistrations}{supplier.otherRegistrations}{/if}
      {:else}
        There are no registration numbers for {supplier.name} yet.
      {/if}
    </p>
    <button type="button" class="link-button" onclick={start}
      >{supplier.passportRegistrationNumber || supplier.otherRegistrations
        ? "Change numbers"
        : "Add numbers"}</button
    >
  {/if}
</div>
