<script lang="ts">
  import type { Snippet } from "svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import { attempt, type Schemas } from "../../lib/types";
  import { plantName, type PickedPlant } from "../../lib/work";
  import PlantSearch from "./PlantSearch.svelte";

  let {
    plantId,
    canManage = false,
    onBack,
    actions,
  }: {
    plantId: string;
    canManage?: boolean;
    onBack: () => void;
    actions?: Snippet;
  } = $props();

  let card = $state<Schemas["PlantCardResponse"] | null>(null);
  let note = $state("");
  let problem = $state("");
  let busy = $state(false);
  let photoVersion = $state(0);

  const setProblem = (message: string) => (problem = message);

  async function load() {
    card =
      (
        await api.GET("/api/plants/{id}/card", {
          params: { path: { id: plantId } },
        })
      ).data ?? null;
  }

  onMount(() => void attempt(load, setProblem));

  const facts = $derived(
    card
      ? (
          [
            ["Family", card.family],
            ["Native to the UK", nativeWords(card.nativeToUk)],
            ["Category", card.category],
            ["Position", card.position],
            ["Watering", card.watering],
            ["Hardiness", card.hardiness],
            ["Xylella host", card.xylellaHost ? "Yes" : null],
            ["Protected variety", card.protectedVariety ? "Yes" : null],
          ] as [string, string | null | undefined][]
        ).filter(([, value]) => value)
      : [],
  );

  function nativeWords(native: boolean | null | undefined) {
    return native === true ? "Yes" : native === false ? "No, introduced" : null;
  }

  function when(date: string) {
    return new Date(date).toLocaleDateString("en-GB", {
      day: "numeric",
      month: "short",
      year: "numeric",
    });
  }

  function creditWords(credit: Schemas["Credited"]) {
    return credit.licence
      ? `${credit.source}, ${credit.licence}`
      : credit.source;
  }

  const addNote = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      busy = true;

      try {
        const { response } = await api.POST("/api/plants/{id}/notes", {
          params: { path: { id: plantId } },
          body: { text: note },
        });

        if (response.ok) {
          note = "";
          problem = "";
          await load();
        } else {
          problem = "Please write something first.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);
  };

  const choosePicture = (event: Event) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    return attempt(async () => {
      const response = await fetch(`/api/plants/${plantId}/photo`, {
        method: "PUT",
        body: await shrinkPhoto(file),
      });

      input.value = "";

      if (response.ok) {
        photoVersion += 1;
        problem = "";
        await load();
      } else {
        problem = "That picture couldn't be saved. Please use a photo.";
      }
    }, setProblem);
  };

  const link = (choice: PickedPlant) => {
    if (choice.kind !== "species") {
      return;
    }

    return attempt(async () => {
      const { response } = await api.PUT("/api/plants/{id}/species", {
        params: { path: { id: plantId } },
        body: { speciesId: choice.id },
      });

      if (response.ok) {
        problem = "";
        await load();
      } else {
        problem = "That plant couldn't be matched. Please try again.";
      }
    }, setProblem);
  };
</script>

<main>
  {#if card}
    <h1>{plantName(card.commonName, card.variety)}</h1>
    {#if card.botanicalName}<p class="plant-botanical">
        <em>{card.botanicalName}</em>
      </p>{/if}

    {#if card.hasPhoto}
      {#key photoVersion}
        <img
          class="plant-photo"
          src="/api/plants/{plantId}/photo?v={photoVersion}"
          alt=""
        />
      {/key}
      {#if card.photoCredit}<p class="muted credit">{card.photoCredit}</p>{/if}
    {/if}

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if card.lookupUnavailable}
      <p class="muted">
        Can't reach the online plant details just now, so some may be missing.
      </p>
    {/if}

    {#if facts.length > 0}
      <dl class="facts">
        {#each facts as [name, value] (name)}
          <dt>{name}</dt>
          <dd>{value}</dd>
        {/each}
      </dl>
    {/if}

    <h2>Growing notes</h2>
    <form class="stack" onsubmit={addNote}>
      <label>
        Add a growing note
        <textarea bind:value={note} rows="3" required></textarea>
      </label>
      <button type="submit" disabled={busy}>Add note</button>
    </form>

    {#each card.notes as item (item.id)}
      <div class="card">
        <p class="plant-note">{item.text}</p>
        <p class="muted credit">{item.addedBy}, {when(item.createdAt)}</p>
      </div>
    {:else}
      <p class="muted">
        No notes yet. Add what you've learned about growing it.
      </p>
    {/each}

    {#if card.description}
      <h2>About this plant</h2>
      <p class="article-text">{card.description.text}</p>
      <p class="muted credit">
        From
        <a href={card.description.url} target="_blank" rel="noopener noreferrer"
          >{creditWords(card.description)}</a
        >
      </p>
    {/if}

    {#if card.growing}
      <h2>Growing it</h2>
      <p class="article-text">{card.growing.text}</p>
      <p class="muted credit">
        From
        <a href={card.growing.url} target="_blank" rel="noopener noreferrer"
          >{creditWords(card.growing)}</a
        >
      </p>
    {/if}

    {#if !card.linkedToSpecies && canManage}
      <h2>Find this plant's details</h2>
      <PlantSearch
        recent={[]}
        onPick={link}
        speciesOnly
        label="Search for this plant"
      />
    {/if}

    <label class="photo-choice">
      <span>{card.hasPhoto ? "Change the picture" : "Add a picture"}</span>
      <input type="file" accept="image/*" onchange={choosePicture} />
    </label>

    {#if actions}{@render actions()}{/if}
  {:else if problem}
    <p class="problem" role="alert">{problem}</p>
  {:else}
    <p class="muted">Loading…</p>
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
