<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import type { Snippet } from "svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import { attempt, type Schemas } from "../../lib/types";
  import { plantName, type PickedPlant } from "../../lib/work";
  import ArticleSection from "./ArticleSection.svelte";
  import Loading from "./Loading.svelte";
  import PlantHero from "./PlantHero.svelte";
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
            [
              "Xylella host",
              card.xylellaHost === true
                ? "Yes"
                : card.xylellaHost === false
                  ? "No"
                  : "Not checked yet",
            ],
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
    <header class="plant-title">
      <h1>{plantName(card.commonName, card.variety)}</h1>
      {#if card.botanicalName}<p class="plant-botanical">
          <em>{card.botanicalName}</em>
        </p>{/if}
    </header>

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if card.lookupUnavailable}
      <p class="muted">
        Can't reach the online plant details just now, so some may be missing.
      </p>
    {/if}

    <div class="plant-layout">
      <aside class="plant-side">
        {#key photoVersion}
          <PlantHero
            url={card.hasPhoto
              ? `/api/plants/${plantId}/photo?v=${photoVersion}`
              : null}
            credit={card.photoCredit}
          />
        {/key}

        {#if facts.length > 0}
          <dl class="facts card">
            {#each facts as [name, value] (name)}
              <dt>{name}</dt>
              <dd>{value}</dd>
            {/each}
          </dl>
        {/if}

        <label class="photo-choice">
          <Camera size={20} />
          <span>{card.hasPhoto ? "Change the picture" : "Add a picture"}</span>
          <input type="file" accept="image/*" onchange={choosePicture} />
        </label>
      </aside>

      <div class="plant-main">
        <section class="stack">
          <h2>Growing notes</h2>
          <form class="card note-form" onsubmit={addNote}>
            <label>
              Add a growing note
              <textarea bind:value={note} rows="3" required></textarea>
            </label>
            <button type="submit" disabled={busy}>Add note</button>
          </form>

          {#each card.notes as item (item.id)}
            <article class="card plant-note-card appear">
              <p class="plant-note">{item.text}</p>
              <p class="muted credit">{item.addedBy}, {when(item.createdAt)}</p>
            </article>
          {:else}
            <p class="note-empty muted">
              No notes yet. Add what you've learned about growing it.
            </p>
          {/each}
        </section>

        {#if card.description}
          <ArticleSection title="About this plant" article={card.description} />
        {/if}
        {#if card.growing}
          <ArticleSection title="Growing it" article={card.growing} />
        {/if}

        {#if !card.linkedToSpecies && canManage}
          <section class="stack">
            <h2>Find this plant's details</h2>
            <PlantSearch
              recent={[]}
              onPick={link}
              speciesOnly
              label="Search for this plant"
            />
          </section>
        {/if}

        {#if actions}
          <div class="plant-actions">{@render actions()}</div>
        {/if}
      </div>
    </div>
  {:else if problem}
    <p class="problem" role="alert">{problem}</p>
  {:else}
    <Loading text="Opening this plant…" />
    <div class="plant-layout" aria-hidden="true">
      <div class="plant-side"><span class="skeleton skeleton-hero"></span></div>
      <div class="plant-main">
        <span class="skeleton-lines">
          <span class="skeleton"></span>
          <span class="skeleton"></span>
          <span class="skeleton short"></span>
        </span>
      </div>
    </div>
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
