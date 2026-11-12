<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { passportProblems } from "../../lib/passports";
  import type { Schemas } from "../../lib/types";
  import PrintButton from "../PrintButton.svelte";
  import PassportCard from "./PassportCard.svelte";
  import Stepper from "./Stepper.svelte";

  // Issuing a UK plant passport for plants of one batch in this tray. GrowLog fills in every part; the
  // person examines the plants, says so, and says where they're going.
  let {
    containerId,
    batchId,
  }: {
    containerId: string;
    batchId: string;
  } = $props();

  let draft = $state<Schemas["IssueDraft"] | null>(null);
  let issued = $state<Schemas["PassportSummary"][]>([]);
  let customers = $state<string[]>([]);
  let issuing = $state(false);
  let quantity = $state(1);
  let issuedTo = $state("");
  let examined = $state(false);
  let findings = $state("");
  let problem = $state("");
  let busy = $state(false);
  let justIssued = $state<string | null>(null);

  const available = $derived(Number(draft?.available ?? 0));
  const ready = $derived(
    !!draft &&
      draft.problems.length === 0 &&
      examined &&
      issuedTo.trim().length > 0 &&
      quantity >= 1 &&
      quantity <= available,
  );

  async function load() {
    const [found, list] = await Promise.all([
      api.GET("/api/batches/{id}/passport-draft", {
        params: { path: { id: batchId }, query: { containerId } },
      }),
      api.GET("/api/passports", { params: { query: { batchId } } }),
    ]);
    draft = found.data ?? null;
    issued = list.data ?? [];
  }

  onMount(async () => {
    try {
      await load();
      // Only the owner keeps customers; for anyone else this is refused, and a name is typed instead.
      const { data } = await api.GET("/api/customers");
      customers = (data ?? []).map((customer) => customer.name);
    } catch {
      problem = "Passports need a connection.";
    }
  });

  function start() {
    quantity = Math.max(1, available);
    issuedTo = "";
    examined = false;
    findings = "";
    problem = "";
    justIssued = null;
    issuing = true;
  }

  async function issue() {
    busy = true;

    try {
      const { data, error, response } = await api.POST("/api/passports", {
        body: {
          batchId,
          containerId,
          quantity,
          customerId: null,
          issuedTo: issuedTo.trim(),
          examined,
          findings: findings.trim() || null,
        },
      });

      if (data) {
        issuing = false;
        justIssued = data.id;
        await load();
      } else {
        problem = passportProblems(
          response.status,
          (error as { problem?: string } | undefined)?.problem,
        );
      }
    } catch {
      problem = "That couldn't be issued. Check the connection.";
    } finally {
      busy = false;
    }
  }
</script>

<section class="stack">
  <h2>Passports issued here</h2>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if issuing && draft}
    <PassportCard
      caption="The passport that will be issued"
      partA={draft.partA}
      partB={draft.partB}
      partC={draft.partC}
      partD={draft.partD}
    >
      <span class="muted">D: {draft.originRule}</span>
    </PassportCard>

    {#if draft.problems.length > 0}
      <div class="caution stack tight" role="alert">
        {#each draft.problems as reason (reason)}<p>{reason}</p>{/each}
      </div>
    {:else}
      {#if draft.xylellaHost === null}
        <p class="caution">
          Nobody has said whether this plant is a Xylella host, so it is treated
          as one. Check it under Plants.
        </p>
      {/if}
      <p class="field-label">How many plants it covers</p>
      <Stepper bind:value={quantity} max={available} />
      <p class="muted">{available} in this tray.</p>
      <label
        >Going to <input
          bind:value={issuedTo}
          list="passport-customers"
          autocomplete="off"
          placeholder="The business the plants go to"
        /></label
      >
      <datalist id="passport-customers">
        {#each customers as name (name)}<option value={name}></option>{/each}
      </datalist>
      <label class="tick">
        <input type="checkbox" bind:checked={examined} />
        I've examined these plants just now and they're fit to move
      </label>
      <label
        >Anything noticed (optional) <input
          bind:value={findings}
          placeholder="No pests or diseases seen"
        /></label
      >
      <p class="muted">
        The examination is recorded on the tray with your name. A passport is
        kept for at least three years and can't be changed: if something is
        wrong, issue another.
      </p>
    {/if}
    <div class="row">
      <button disabled={busy || !ready} onclick={issue}>Issue passport</button>
      <button class="secondary" onclick={() => (issuing = false)}>Cancel</button
      >
    </div>
  {:else}
    <button onclick={start} disabled={!draft}>Issue a passport</button>
  {/if}

  <div class="passport-list">
    {#each issued as passport (passport.id)}
      <div class="stack tight">
        <PassportCard
          caption="Passport issued {dayLabel(
            passport.issuedOn,
          )} to {passport.issuedTo}"
          partA={passport.partA}
          partB={passport.partB}
          partC={passport.partC}
          partD={passport.partD}
        >
          <span
            >{passport.quantity} to {passport.issuedTo}, {dayLabel(
              passport.issuedOn,
            )}</span
          >
          <span class="muted"
            >From {passport.labelCode}{passport.issuedBy
              ? `, issued by ${passport.issuedBy}`
              : ""}</span
          >
        </PassportCard>
        {#if justIssued === passport.id}
          <p class="card done" role="status">Passport issued.</p>
        {/if}
        <PrintButton
          href="/api/labels/passports/{passport.id}"
          label="Print passport"
        />
      </div>
    {:else}
      {#if !issuing}<p class="muted">None issued for these plants yet.</p>{/if}
    {/each}
  </div>
</section>
