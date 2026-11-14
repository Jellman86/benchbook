<script lang="ts">
  import { api } from "../../api/client";
  import {
    placeKinds,
    type AddedPlace,
    type Place,
    type PlaceKind,
  } from "../../lib/places";
  import { attempt } from "../../lib/types";

  // Adds a place, or a numbered row of them such as Bench 1 to Bench 12. A single place is handed back, so it can be
  // drawn on the map next.
  let {
    places,
    inside = null,
    onAdded,
  }: {
    places: Place[];
    inside?: string | null;
    onAdded: (added: AddedPlace | null) => void | Promise<void>;
  } = $props();

  let kind = $state<PlaceKind>("Bench");
  let parentId = $state("");
  let name = $state("");
  let several = $state(false);
  let from = $state(1);
  let to = $state(6);
  let problem = $state("");

  const parent = $derived(inside ?? (kind === "Site" ? null : parentId));
  const choices = $derived(
    places
      .filter((place) => !place.archived)
      .sort((a, b) => a.path.localeCompare(b.path, "en-GB", { numeric: true })),
  );
  const kinds = $derived(
    inside ? placeKinds.filter((item) => item.kind !== "Site") : placeKinds,
  );

  const setProblem = (message: string) => (problem = message);

  function explain(status: number) {
    problem =
      status === 409
        ? "There's already a place with that name there."
        : several
          ? "Please check the name and the numbers (up to 50 at once)."
          : "Please check the name and where it belongs.";
  }

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { data, response } =
        several && parent
          ? await api.POST("/api/locations/several", {
              body: {
                kind,
                parentId: parent,
                prefix: name,
                from: Number(from),
                to: Number(to),
              },
            })
          : await api.POST("/api/locations", {
              body: { name, kind, parentId: parent },
            });

      if (response.ok) {
        const added =
          data && "id" in data
            ? { id: data.id, name: name.trim(), kind, parentId: parent }
            : null;
        name = "";
        problem = "";
        await onAdded(added);
      } else {
        explain(response.status);
      }
    }, setProblem);
  };
</script>

<form class="stack" onsubmit={add}>
  <label>
    Type
    <select bind:value={kind}>
      {#each kinds as option (option.kind)}
        <option value={option.kind}>{option.label}</option>
      {/each}
    </select>
  </label>
  {#if !inside && kind !== "Site"}
    <label>
      Inside
      <select bind:value={parentId} required>
        <option value="">Choose…</option>
        {#each choices as choice (choice.id)}
          <option value={choice.id}>{choice.path}</option>
        {/each}
      </select>
    </label>
  {/if}
  <label>
    {several ? "Name before the number" : "Name"}
    <input
      bind:value={name}
      required
      placeholder={several ? "Bench" : "Greenhouse 1"}
    />
  </label>
  {#if kind !== "Site"}
    <label class="tick">
      <input type="checkbox" bind:checked={several} /> Add several, numbered
    </label>
  {/if}
  {#if several && kind !== "Site"}
    <div class="field-grid">
      <label>From <input type="number" min="0" bind:value={from} /></label>
      <label>To <input type="number" min="0" bind:value={to} /></label>
    </div>
    <p class="muted">
      This adds {name || "Bench"}
      {from} to {name || "Bench"}
      {to}.
    </p>
  {/if}
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button type="submit"
    >{several && kind !== "Site" ? "Add them" : "Add"}</button
  >
</form>
