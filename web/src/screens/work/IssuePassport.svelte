<script lang="ts">
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { issueHint, passportProblems } from "../../lib/passports";
  import type { Schemas } from "../../lib/types";
  import PrintButton from "../PrintButton.svelte";
  import PassportCard from "./PassportCard.svelte";
  import Stepper from "./Stepper.svelte";

  // Issuing a UK plant passport for plants of one batch in this tray. Hortiplanner fills in every part; the
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
  let activeGrowthConfirmed = $state(false);
  let examined = $state(false);
  let findings = $state("");
  let problem = $state("");
  let busy = $state(false);
  let justIssued = $state<string | null>(null);
  // Made once per passport, so a retry after a lost reply finds the one already issued.
  let passportId = crypto.randomUUID();

  const available = $derived(Number(draft?.available ?? 0));
  const hint = $derived(
    issueHint({
      examined,
      issuedTo,
      quantity,
      available,
      blocked: (draft?.problems.length ?? 0) > 0,
    }),
  );

  async function loadDraft() {
    draft =
      (
        await api.GET("/api/batches/{id}/passport-draft", {
          params: { path: { id: batchId }, query: { containerId } },
        })
      ).data ?? null;
  }

  async function loadIssued() {
    issued =
      (await api.GET("/api/passports", { params: { query: { batchId } } }))
        .data ?? [];
  }

  onMount(async () => {
    try {
      await Promise.all([loadDraft(), loadIssued()]);
      // Only the owner keeps customers; for anyone else this is refused, and a name is typed instead.
      const { data } = await api.GET("/api/customers");
      customers = (data ?? []).map((customer) => customer.name);
    } catch {
      problem = "Passports need a connection.";
    }
  });

  // Worked out afresh each time, as Part D can change with time.
  async function start() {
    problem = "";
    justIssued = null;

    try {
      await loadDraft();
    } catch {
      problem = "Passports need a connection.";
      return;
    }

    quantity = Math.max(1, available);
    issuedTo = "";
    examined = false;
    activeGrowthConfirmed = false;
    findings = "";
    passportId = crypto.randomUUID();
    issuing = true;
  }

  async function showIssued(id: string) {
    issuing = false;
    justIssued = id;
    await loadIssued();
    await tick();
    document
      .getElementById(`passport-${id}`)
      ?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  async function issue() {
    busy = true;
    problem = "";

    try {
      const { data, error, response } = await api.POST("/api/passports", {
        body: {
          passportId,
          batchId,
          containerId,
          quantity,
          customerId: null,
          issuedTo: issuedTo.trim(),
          examined,
          findings: findings.trim() || null,
          expectedPartD:
            activeGrowthConfirmed && draft?.needsActiveGrowthConfirmation
              ? "GB"
              : (draft?.partD ?? null),
          activeGrowthConfirmed,
        },
      });

      if (data) {
        await showIssued(data.id);
        return;
      }

      const reason = (error as { problem?: string } | undefined)?.problem;
      problem = passportProblems(response.status, reason);

      if (reason === "Changed") {
        await loadDraft();
      }
    } catch {
      // The reply may have been lost after it was issued, so look before saying it wasn't.
      try {
        await loadIssued();
      } catch {
        // Still no signal: the message below says so.
      }

      if (issued.some((passport) => passport.id === passportId)) {
        await showIssued(passportId);
      } else {
        problem =
          "That couldn't be issued. Check the connection and try again.";
      }
    } finally {
      busy = false;
    }
  }
</script>

<section class="stack">
  <h2>Passports issued here</h2>

  {#if issuing && draft}
    <PassportCard
      caption="The passport that will be issued"
      partA={draft.partA}
      partB={draft.partB}
      partC={draft.partC}
      partD={activeGrowthConfirmed && draft.needsActiveGrowthConfirmation
        ? "GB"
        : draft.partD}
    >
      <span class="muted">D: {draft.originRule}</span>
    </PassportCard>

    {#if draft.propagatingMaterial}
      <p class="caution">
        Ornamental propagating material, including seeds, plugs, cuttings and
        liners, must also travel with a separate supplier document. A plant
        passport does not replace it.
      </p>
    {/if}
    {#if draft.needsActiveGrowthConfirmation}
      <label class="tick"
        ><input type="checkbox" bind:checked={activeGrowthConfirmed} />
        I confirm these plants were in active growth for the time described above.
        Leave unticked to keep their original country.</label
      >
    {/if}
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
          autocapitalize="words"
          enterkeyhint="done"
          maxlength="200"
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
          maxlength="200"
          enterkeyhint="done"
          placeholder="No pests or diseases seen"
        /></label
      >
      <p class="muted">
        The examination is recorded on the tray with your name. A passport is
        kept for at least three years and can't be changed: if something is
        wrong, issue another.
      </p>
    {/if}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if hint && !problem}<p class="muted">{hint}</p>{/if}
    <div class="row">
      <button disabled={busy || !!hint} onclick={issue}>Issue passport</button>
      <button class="secondary" onclick={() => (issuing = false)}>Cancel</button
      >
    </div>
  {:else}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <button onclick={start} disabled={!draft}>Issue a passport</button>
  {/if}

  <div class="passport-list">
    {#each issued as passport (passport.id)}
      <div class="stack tight" id="passport-{passport.id}">
        {#if justIssued === passport.id}
          <p class="card done" role="status">
            Passport issued. Print it to go with the plants.
          </p>
        {/if}
        <PassportCard
          caption="Passport issued {dayLabel(passport.issuedOn)}"
          partA={passport.partA}
          partB={passport.partB}
          partC={passport.partC}
          partD={passport.partD}
        >
          <span
            >{passport.quantity} plants{passport.issuedTo
              ? ` to ${passport.issuedTo}`
              : ""}, {dayLabel(passport.issuedOn)}</span
          >
          <span class="muted"
            >From {passport.labelCode}{passport.issuedBy
              ? `, issued by ${passport.issuedBy}`
              : ""}</span
          >
        </PassportCard>
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
