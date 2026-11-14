<script lang="ts">
  import { isOn } from "../../lib/features.svelte";
  import ImageUp from "@lucide/svelte/icons/image-up";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";
  import { isoDay } from "../../lib/days";
  import DayPicker from "../DayPicker.svelte";

  // What invoices, labels and plant passports say about the nursery.
  let { onBack }: { onBack: () => void } = $props();

  let details = $state<Schemas["NurseryDetails"]>({
    name: null,
    address: null,
    phone: null,
    email: null,
    website: null,
    registrationNumber: null,
    vatNumber: null,
    hasLogo: false,
    vatRegistered: false,
    paymentTermsDays: 30,
    authorisationRenewsOn: null,
    setsRenewal: true,
  });
  let logoVersion = $state(0);
  // When the plant health authorisation renews, or empty.
  let renewsOn = $state("");
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    const { data } = await api.GET("/api/settings/nursery");
    details = data ?? details;
    renewsOn = details.authorisationRenewsOn ?? "";
  }

  onMount(() => void attempt(load, setProblem));

  const saveDetails = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/settings/nursery", {
        body: {
          ...details,
          authorisationRenewsOn: renewsOn || null,
          setsRenewal: true,
        },
      });
      saved = response.ok ? "Nursery details saved." : "";
      problem = response.ok ? "" : "Those details couldn't be saved.";
    }, setProblem);
  };

  const chooseLogo = (event: Event) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    return attempt(async () => {
      const response = await fetch("/api/settings/nursery/logo", {
        method: "PUT",
        body: file,
      });
      input.value = "";
      problem = response.ok
        ? ""
        : "The logo must be a PNG or JPEG picture under 2MB.";
      logoVersion += 1;
      await load();
    }, setProblem);
  };

  const removeLogo = () =>
    attempt(async () => {
      await api.DELETE("/api/settings/nursery/logo");
      logoVersion += 1;
      await load();
    }, setProblem);
</script>

<main>
  <h1>Nursery details</h1>
  <p class="muted">Shown on invoices, labels and plant passports.</p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="split">
    <section>
      <form class="form-card stack" onsubmit={saveDetails}>
        <h2 class="form-heading">The nursery</h2>
        <label>Name <input bind:value={details.name} /></label>
        <label
          >Address <textarea bind:value={details.address} rows="3"
          ></textarea></label
        >
        <div class="field-grid">
          <label
            >Phone <input bind:value={details.phone} inputmode="tel" /></label
          >
          <label>Email <input bind:value={details.email} type="email" /></label>
        </div>
        <label>Website <input bind:value={details.website} /></label>
        <div class="field-grid">
          <label
            >Plant health registration <input
              bind:value={details.registrationNumber}
            /></label
          >
          <label>VAT number <input bind:value={details.vatNumber} /></label>
        </div>
        <label class="tick">
          <input type="checkbox" bind:checked={details.vatRegistered} />
          Registered for VAT, so invoices charge it
        </label>
        <label
          >Days customers have to pay <input
            type="number"
            min="0"
            max="365"
            bind:value={details.paymentTermsDays}
          /></label
        >
        {#if isOn("Passports")}
          {#if renewsOn}
            <DayPicker
              bind:value={renewsOn}
              label="Plant health authorisation renews on"
              max={null}
            />
            <button
              type="button"
              class="link-button"
              onclick={() => (renewsOn = "")}>No renewal date</button
            >
          {:else}
            <button
              type="button"
              class="secondary"
              onclick={() => (renewsOn = isoDay(new Date()))}
              >Add the authorisation renewal date</button
            >
          {/if}
          <p class="muted">
            Hortiplanner reminds you six weeks before, so the inspection can be
            booked.
          </p>
        {/if}
        <button type="submit">Save details</button>
      </form>
    </section>

    <section>
      <div class="form-card stack">
        <h2 class="form-heading">Logo</h2>
        {#if details.hasLogo}
          {#key logoVersion}
            <img
              class="logo-preview"
              src="/api/settings/nursery/logo?v={logoVersion}"
              alt="The nursery's logo"
            />
          {/key}
        {/if}
        <p class="muted">
          A one-colour logo prints best on a thermal label printer.
        </p>
        <label class="photo-choice">
          <ImageUp size={20} />
          <span>{details.hasLogo ? "Change the logo" : "Add a logo"}</span>
          <input
            type="file"
            accept="image/png,image/jpeg"
            onchange={chooseLogo}
          />
        </label>
        {#if details.hasLogo}
          <button class="link-button" onclick={removeLogo}
            >Remove the logo</button
          >
        {/if}
      </div>
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
