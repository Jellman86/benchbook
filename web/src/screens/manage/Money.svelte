<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { attempt, type Schemas } from "../../lib/types";
  import { money } from "../../lib/work";

  let { onBack }: { onBack: () => void } = $props();

  const today = () => new Date().toISOString().slice(0, 10);

  const monthName = (month: string) =>
    new Date(`${month}-01T12:00:00Z`).toLocaleDateString("en-GB", {
      month: "long",
      year: "numeric",
    });

  let takings = $state<Schemas["TakingResponse"][]>([]);
  let summary = $state<Schemas["MoneySummary"] | null>(null);
  let takenOn = $state(today());
  let amount = $state("");
  let notes = $state("");
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    takings = (await api.GET("/api/money/takings")).data ?? [];
    summary = (await api.GET("/api/money/summary")).data ?? null;
  }

  onMount(() => attempt(load, setProblem));

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/money/takings", {
        body: { takenOn, amount: Number(amount), notes: notes || null },
      });

      if (response.ok) {
        amount = "";
        notes = "";
        await load();
      } else {
        problem =
          response.status === 409
            ? "That day is already entered. Void it first to enter it again."
            : "Please check the date and amount.";
      }
    }, setProblem);
  };

  const voidTaking = (taking: Schemas["TakingResponse"]) => {
    if (!confirm(`Void the takings for ${dayLabel(taking.takenOn)}?`)) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/money/takings/{id}/void", {
        params: { path: { id: taking.id } },
      });
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Money</h1>

  <div class="split">
    <section>
      <form class="stack" onsubmit={save}>
        <label>Date <input type="date" bind:value={takenOn} required /></label>
        <label
          >Amount taken (£) <input
            bind:value={amount}
            inputmode="decimal"
            required
          /></label
        >
        <label>Notes <input bind:value={notes} /></label>
        <button type="submit">Save takings</button>
      </form>

      <a class="button secondary" href="/api/money/export.csv" download
        >Download for accountant (CSV)</a
      >

      <h2>Recent takings</h2>
      {#each takings as taking (taking.id)}
        <div class="card recent">
          <span class="stack-tight">
            <strong
              >{dayLabel(taking.takenOn)}: {money(
                Number(taking.amount),
              )}</strong
            >
            {#if taking.notes}<span class="muted">{taking.notes}</span>{/if}
          </span>
          <button class="link-button" onclick={() => voidTaking(taking)}
            >Void</button
          >
        </div>
      {:else}
        <p class="muted">No takings entered yet.</p>
      {/each}
    </section>

    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#if summary}
        <div class="stat-tiles">
          <div class="stat-tile">
            <strong>{money(Number(summary.owed))}</strong><span
              >still owed to suppliers</span
            >
          </div>
          <div class="stat-tile">
            <strong>{money(Number(summary.owedToUs))}</strong><span
              >still owed by customers</span
            >
          </div>
        </div>
        {#each summary.months as month (month.month)}
          <div class="card">
            <strong>{monthName(month.month)}</strong>
            <span>Income: {money(Number(month.income))}</span>
            <span>Spending: {money(Number(month.spending))}</span>
          </div>
        {/each}
      {/if}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
