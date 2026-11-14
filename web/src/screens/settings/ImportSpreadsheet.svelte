<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    decodeSpreadsheet,
    mappedRows,
    matchColumns,
    readRows,
    type ImportField,
  } from "../../lib/spreadsheet";
  import { attempt, type Schemas } from "../../lib/types";

  // Plants, suppliers or customers brought in from a spreadsheet saved as CSV. Its columns are matched to
  // Hortiplanner's, every row is checked, and nothing is saved while any row has a problem.
  let { onDone }: { onDone: (message: string) => void } = $props();

  type Kind = Schemas["ImportKind"];

  const kinds: { kind: Kind; label: string; one: string }[] = [
    { kind: "Plants", label: "Plants", one: "plant" },
    { kind: "Suppliers", label: "Suppliers", one: "supplier" },
    { kind: "Customers", label: "Customers", one: "customer" },
  ];

  let catalogue = $state<Schemas["ImportKindFields"][]>([]);
  let kind = $state<Kind>("Plants");
  let fileName = $state("");
  let header = $state<string[]>([]);
  let body = $state<string[][]>([]);
  let matched = $state<Record<string, number>>({});
  let leftOut = $state<number[]>([]);
  // Columns found in the spreadsheet show straight away; the rest wait behind a button, as a phone has little room.
  let found = $state<string[]>([]);
  let showAll = $state(false);
  let result = $state<Schemas["ImportResult"] | null>(null);
  let busy = $state(false);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  // A file can be chosen before Hortiplanner's columns have arrived; matching waits for them.
  let fieldsLoaded: Promise<unknown> = Promise.resolve();

  onMount(() => {
    fieldsLoaded = attempt(async () => {
      catalogue = (await api.GET("/api/import/fields")).data ?? [];
    }, setProblem);
  });

  const fields = $derived<ImportField[]>(
    catalogue.find((item) => item.kind === kind)?.fields ?? [],
  );
  const one = $derived(kinds.find((item) => item.kind === kind)!.one);
  const counted = (count: number | string, what = "") =>
    `${count} ${what}${one}${Number(count) === 1 ? "" : "s"}`;
  const rows = $derived(
    mappedRows(body, matched).filter((row) => !leftOut.includes(row.row)),
  );
  const shown = $derived(
    fields.filter(
      (field) => showAll || field.required || found.includes(field.key),
    ),
  );
  const missing = $derived(
    fields.filter((field) => field.required && (matched[field.key] ?? -1) < 0),
  );

  function choose(next: Kind) {
    kind = next;
    result = null;
    leftOut = [];
    const kindFields =
      catalogue.find((item) => item.kind === next)?.fields ?? [];
    matched = matchColumns(header, kindFields);
    foundFrom(matched);
  }

  function foundFrom(columns: Record<string, number>) {
    found = Object.keys(columns).filter((key) => columns[key] >= 0);
    showAll = false;
  }

  async function read(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    problem = "";
    result = null;
    leftOut = [];
    const all = readRows(decodeSpreadsheet(await file.arrayBuffer()));
    await fieldsLoaded;

    if (all.length < 2) {
      problem =
        "That file has no rows under its headings. Save the spreadsheet as CSV and try again.";
      header = [];
      body = [];
      return;
    }

    fileName = file.name;
    header = all[0].map((name) => name.trim());
    body = all.slice(1);
    matched = matchColumns(header, fields);
    foundFrom(matched);
  }

  const send = (save: boolean) =>
    attempt(async () => {
      busy = true;

      try {
        const { data, response } = await api.POST("/api/import/{kind}", {
          params: { path: { kind } },
          body: { rows, save },
        });

        if (data) {
          result = data;
          problem =
            response.status === 409
              ? "Some of these were added by someone else just now. Please check them again."
              : "";

          if (data.saved) {
            onDone(
              `${counted(data.adding)} ${Number(data.adding) === 1 ? "was" : "were"} brought in from ${fileName}.`,
            );
            header = [];
            body = [];
            result = null;
          }
        } else {
          problem =
            response.status === 400
              ? "Up to 2,000 rows can be brought in at once."
              : "Those rows couldn't be checked. Please try again.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);

  function leaveOutProblems() {
    leftOut = [
      ...leftOut,
      ...(result?.problems ?? []).map((item) => Number(item.row)),
    ];
    void send(false);
  }

  const columnOf = (key: string) => matched[key] ?? -1;
</script>

<div class="form-card stack">
  <h2 class="form-heading">Bring in from a spreadsheet</h2>
  <p class="muted">
    Save the spreadsheet as CSV first (in Excel, Numbers or Google Sheets:
    Export or Download, then CSV). The first row should be the column headings.
    Anything already recorded is left as it is.
  </p>
  <fieldset class="day-picker">
    <legend class="field-label">What's in it?</legend>
    <div class="segmented">
      {#each kinds as item (item.kind)}
        <button
          type="button"
          class={kind === item.kind ? "" : "secondary"}
          aria-pressed={kind === item.kind}
          onclick={() => choose(item.kind)}>{item.label}</button
        >
      {/each}
    </div>
  </fieldset>
  <label
    >Spreadsheet (CSV) <input
      type="file"
      accept=".csv,.tsv,.txt,text/csv,text/tab-separated-values"
      onchange={read}
    /></label
  >

  {#if header.length > 0}
    <p>
      <strong>{fileName}</strong> has {body.length} row{body.length === 1
        ? ""
        : "s"}. Check which column each heading goes in.
    </p>
    <div class="field-grid">
      {#each shown as field (field.key)}
        <label
          >{field.label}{field.required ? "" : " (optional)"}
          <select
            value={columnOf(field.key)}
            onchange={(event) => {
              matched = {
                ...matched,
                [field.key]: Number(event.currentTarget.value),
              };
              result = null;
            }}
          >
            <option value={-1}>Not in the spreadsheet</option>
            {#each header as name, index (index)}
              <option value={index}>{name || `Column ${index + 1}`}</option>
            {/each}
          </select></label
        >
      {/each}
    </div>
    {#if shown.length < fields.length}
      <button type="button" class="link-button" onclick={() => (showAll = true)}
        >Match more columns ({fields.length - shown.length} more)</button
      >
    {/if}
    {#if missing.length > 0}
      <p class="problem">
        Choose the column for {missing.map((field) => field.label).join(", ")}.
      </p>
    {/if}
    {#if leftOut.length > 0}
      <p class="muted">
        {leftOut.length === 1 ? "Row" : "Rows"}
        {leftOut.join(", ")}
        {leftOut.length === 1 ? "is" : "are"} being left out.
      </p>
    {/if}
  {/if}

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if result && !result.saved}
    <div class="card stack tight" role="status">
      <p>
        <strong
          >{counted(result.adding, "new ")}{result.problems.length > 0
            ? ", but nothing will be saved until every row is right"
            : " to bring in"}.</strong
        >
        {#if result.alreadyThere.length > 0}
          {result.alreadyThere.length === 1
            ? "1 is already recorded and will be left as it is."
            : `${result.alreadyThere.length} are already recorded and will be left as they are.`}
        {/if}
      </p>
      {#if result.problems.length > 0}
        <ul class="plain-list">
          {#each result.problems as item (item.row)}
            <li><strong>Row {item.row}:</strong> {item.problem}</li>
          {/each}
        </ul>
        <p class="muted">
          Put them right in the spreadsheet and choose it again, or leave those
          rows out.
        </p>
      {/if}
    </div>
  {/if}

  {#if header.length > 0}
    <div class="row">
      {#if result && result.problems.length > 0}
        <button disabled={busy} onclick={leaveOutProblems}
          >Leave out those rows</button
        >
      {:else if result && Number(result.adding) > 0}
        <button disabled={busy} onclick={() => send(true)}
          >Bring in {counted(result.adding)}</button
        >
      {:else}
        <button
          disabled={busy || missing.length > 0 || rows.length === 0}
          onclick={() => send(false)}>Check the rows</button
        >
      {/if}
    </div>
  {/if}
</div>
