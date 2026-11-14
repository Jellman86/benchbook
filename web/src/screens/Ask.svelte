<script lang="ts">
  import RotateCcw from "@lucide/svelte/icons/rotate-ccw";
  import Camera from "@lucide/svelte/icons/camera";
  import Coins from "@lucide/svelte/icons/coins";
  import SendHorizontal from "@lucide/svelte/icons/send-horizontal";
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import X from "@lucide/svelte/icons/x";
  import { tick } from "svelte";
  import { api } from "../api/client";
  import {
    answerBlocks,
    assistant,
    dollars,
    type AskAbout,
    type AskSource,
  } from "../lib/assistant.svelte";
  import { isOn } from "../lib/features.svelte";
  import { shrinkPhoto } from "../lib/photo";

  // Ask Hortiplanner: questions about plants, the nursery's records and photos of plants. The conversation stays on this
  // device for the session; the server keeps only what each question cost, who asked and when.
  let {
    me,
    about = null,
    onOpenTray,
    onOpenPlace,
  }: {
    me: { id: string };
    about?: AskAbout | null;
    onOpenTray: (label: string) => void;
    onOpenPlace: (id: string) => void;
  } = $props();

  type Line = {
    role: "user" | "assistant";
    text: string;
    photos?: string[];
    sources?: AskSource[];
    cost?: number;
  };

  const storageKey = $derived(`hortiplanner.ask.${me.id}`);
  const finePointer = matchMedia("(pointer: fine)").matches;
  const mostPhotos = 4;

  let lines = $state<Line[]>(remembered());
  let question = $state("");
  let photos = $state<string[]>([]);
  // What the question is about, until it's been asked once. Taken once, when the screen opens.
  // svelte-ignore state_referenced_locally
  let context = $state<AskAbout | null>(about);
  let busy = $state(false);
  let problem = $state("");
  let list = $state<HTMLOListElement | null>(null);
  let box = $state<HTMLTextAreaElement | null>(null);

  function remembered(): Line[] {
    try {
      const kept: unknown = JSON.parse(
        sessionStorage.getItem(`hortiplanner.ask.${me.id}`) ?? "[]",
      );
      return Array.isArray(kept) ? (kept as Line[]) : [];
    } catch {
      return [];
    }
  }

  function keep() {
    try {
      // Photos are left out: they're large, and the answer already says what was in them.
      sessionStorage.setItem(
        storageKey,
        JSON.stringify(lines.map((line) => ({ ...line, photos: undefined }))),
      );
    } catch {
      // Without storage the conversation lasts until the screen is left.
    }
  }

  // Every source looked at across the conversation, for the side panel on a desk.
  const seen = $derived(
    lines
      .flatMap((line) => line.sources ?? [])
      .filter(
        (source, index, all) =>
          all.findIndex(
            (other) => other.kind === source.kind && other.key === source.key,
          ) === index,
      ),
  );

  const suggestions = $derived(
    [
      "What needs attention this week?",
      "Which sown trays have waited longest without germinating?",
      "What did we lose in the last month, and why?",
      isOn("Climate") ? "How cold did the greenhouses get this week?" : null,
      "How do I take lavender cuttings, and when?",
    ].filter((item): item is string => item !== null),
  );

  async function addPhoto(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const files = [...(input.files ?? [])].slice(0, mostPhotos - photos.length);
    input.value = "";

    for (const file of files) {
      const shrunk = await shrinkPhoto(file, 1600);
      photos = [
        ...photos,
        await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result));
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(shrunk);
        }),
      ];
    }
  }

  const refusals: Record<string, string> = {
    NotSetUp:
      "Ask Hortiplanner isn't set up yet. The owner adds an OpenRouter key and chooses a model under Settings, AI assistant.",
    TooMany:
      "That's a lot of questions this hour. Please wait a while before asking more.",
    KeyRefused:
      "OpenRouter turned the key away: it may be out of credit or past its limit. The owner can check it at openrouter.ai.",
    ModelUnavailable:
      "The chosen AI model can't answer questions here. The owner can choose another under Settings, AI assistant.",
    Unavailable: "The AI service didn't answer. Please try again in a minute.",
  };

  async function ask(text = question) {
    const wanted = text.trim();

    if (!wanted || busy) {
      return;
    }

    problem = "";
    busy = true;
    const sent = photos;
    lines = [...lines, { role: "user", text: wanted, photos: sent }];
    question = "";
    photos = [];
    await scrollDown();

    try {
      const { data, response, error } = await api.POST("/api/assistant/ask", {
        body: {
          // The latest lines are enough for the model to follow, and keep a long conversation working.
          messages: lines.slice(-12).map((line) => ({
            role: line.role,
            text: line.text.slice(0, 4000),
          })),
          images: sent.length > 0 ? sent : null,
          about: context
            ? {
                trayLabel: context.trayLabel ?? null,
                plantId: context.plantId ?? null,
                photoId: context.photoId ?? null,
              }
            : null,
        },
      });

      if (data) {
        lines = [
          ...lines,
          {
            role: "assistant",
            text: data.answer,
            sources: data.sources,
            cost: Number(data.cost),
          },
        ];
        assistant.spentThisMonth = Number(data.spentThisMonth ?? 0);
        assistant.spentInAll = Number(data.spentInAll ?? 0);
        context = null;
        keep();
      } else {
        const reason = (error as { problem?: string } | undefined)?.problem;
        problem =
          (reason && refusals[reason]) ??
          (response.status === 403
            ? "The owner hasn't let staff use Ask Hortiplanner."
            : "That couldn't be asked. Please try again.");
        // The question goes back in the box, so it isn't lost.
        lines = lines.slice(0, -1);
        question = wanted;
        photos = sent;
      }
    } catch {
      problem = "Can't reach Hortiplanner. Check the connection and try again.";
      lines = lines.slice(0, -1);
      question = wanted;
      photos = sent;
    } finally {
      busy = false;
      await scrollDown();

      // On a phone, focusing would bring the keyboard back over the answer.
      if (finePointer) {
        box?.focus();
      }
    }
  }

  async function scrollDown() {
    await tick();
    list?.lastElementChild?.scrollIntoView({
      block: "end",
      behavior: "smooth",
    });
  }

  function startAgain() {
    lines = [];
    context = null;
    problem = "";
    keep();
  }

  function openSource(source: AskSource) {
    if (source.kind === "tray") {
      onOpenTray(source.key);
    } else if (source.kind === "place") {
      onOpenPlace(source.key);
    }
  }

  const money = (cost: number) => dollars(cost);
</script>

<!-- What this person's questions have cost: the nursery pays for every one, so it's always in view. -->
{#snippet spend(compact: boolean)}
  {#if assistant.spentThisMonth !== null}
    {#if compact}
      <p class="ask-spend" role="status">
        <Coins size={16} aria-hidden="true" />
        <span
          >Your questions this month: <strong
            >{dollars(assistant.spentThisMonth)}</strong
          >, paid for by the nursery</span
        >
      </p>
    {:else}
      <div class="card stack tight ask-spend-card">
        <h2 class="form-heading">What your questions cost</h2>
        <p>
          This month <strong>{dollars(assistant.spentThisMonth)}</strong>, in
          all <strong>{dollars(assistant.spentInAll ?? 0)}</strong>.
        </p>
        <p class="muted">
          The nursery pays for every question, and the owner can see this total
          too. Photos, long conversations and questions that need a lot looked
          up cost more.
        </p>
      </div>
    {/if}
  {/if}
{/snippet}

{#snippet sourceList(sources: AskSource[])}
  <ul class="ask-sources">
    {#each sources as source (`${source.kind}/${source.key}`)}
      <li>
        {#if source.kind === "tray" || source.kind === "place"}
          <button class="pill-button" onclick={() => openSource(source)}
            >{source.label}</button
          >
        {:else}
          <span class="pill">{source.label}</span>
        {/if}
      </li>
    {/each}
  </ul>
{/snippet}

<main class="ask">
  <div class="ask-layout">
    <section class="ask-conversation" aria-label="Conversation">
      <div class="title-row">
        <h1><Sparkles size={26} aria-hidden="true" /> Ask Hortiplanner</h1>
        {#if lines.length > 0}
          <button class="link-button" onclick={startAgain}
            ><RotateCcw size={16} aria-hidden="true" />Start again</button
          >
        {/if}
      </div>
      {#if assistant.spentThisMonth !== null}
        <div class="mobile-only">{@render spend(true)}</div>
      {/if}

      {#if lines.length === 0}
        <p class="muted">
          Ask about plants, or about the nursery's own trays, losses, treatments{isOn(
            "Climate",
          )
            ? ", climate"
            : ""} and jobs. It reads the records but never changes them, and it can
          be wrong, so check before acting on it.
        </p>
        <div class="ask-suggestions mobile-only">
          {#each suggestions as suggestion (suggestion)}
            <button class="secondary" onclick={() => ask(suggestion)}
              >{suggestion}</button
            >
          {/each}
        </div>
      {/if}

      <ol class="ask-lines" bind:this={list} aria-live="polite">
        {#each lines as line, index (index)}
          <li class="ask-line {line.role}">
            {#if line.role === "user"}
              <p class="ask-question">{line.text}</p>
              {#if line.photos?.length}
                <div class="ask-photos">
                  {#each line.photos as photo, photoIndex (photoIndex)}
                    <img src={photo} alt="Sent with the question" />
                  {/each}
                </div>
              {/if}
            {:else}
              <div class="ask-answer card">
                {#each answerBlocks(line.text) as block, blockIndex (blockIndex)}
                  {#if block.kind === "list"}
                    <ul>
                      {#each block.items as item, itemIndex (itemIndex)}<li>
                          {item}
                        </li>{/each}
                    </ul>
                  {:else}
                    <p>{block.text}</p>
                  {/if}
                {/each}
                {#if line.sources?.length}
                  <div class="ask-looked mobile-only">
                    <span class="muted">Looked at</span>
                    {@render sourceList(line.sources)}
                  </div>
                {/if}
                {#if line.cost != null}
                  <span class="muted ask-cost">Cost {money(line.cost)}</span>
                {/if}
              </div>
            {/if}
          </li>
        {/each}
        {#if busy}
          <li class="ask-line assistant">
            <p class="ask-answer card muted" role="status">
              Looking through the records…
            </p>
          </li>
        {/if}
      </ol>

      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      <form
        class="ask-composer"
        onsubmit={(event) => {
          event.preventDefault();
          void ask();
        }}
      >
        {#if context}
          <p class="ask-context">
            <span class="pill">About {context.title}</span>
            <button
              type="button"
              class="icon-button"
              aria-label="Not about {context.title}"
              onclick={() => (context = null)}><X size={16} /></button
            >
          </p>
        {/if}
        {#if photos.length > 0}
          <div class="ask-photos">
            {#each photos as photo, index (index)}
              <span class="ask-photo">
                <img src={photo} alt="To send with the question" />
                <button
                  type="button"
                  class="icon-button"
                  aria-label="Remove this photo"
                  onclick={() =>
                    (photos = photos.filter((_, other) => other !== index))}
                  ><X size={16} /></button
                >
              </span>
            {/each}
          </div>
        {/if}
        <div class="ask-input">
          {#if assistant.readsImages && photos.length < mostPhotos}
            <label class="icon-button ask-camera" title="Add a photo">
              <Camera size={22} />
              <span class="visually-hidden">Add a photo</span>
              <input
                type="file"
                accept="image/*"
                multiple
                onchange={addPhoto}
              />
            </label>
          {/if}
          <label class="grow">
            <span class="visually-hidden">Your question</span>
            <textarea
              bind:this={box}
              bind:value={question}
              rows="2"
              maxlength="4000"
              placeholder={photos.length > 0
                ? "What would you like to know about the photo?"
                : "Ask about plants or the nursery…"}
              onkeydown={(event) => {
                // On a desk Enter asks; on a phone it starts a new line, and the button asks.
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.isComposing &&
                  finePointer
                ) {
                  event.preventDefault();
                  void ask();
                }
              }}></textarea>
          </label>
          <button
            type="submit"
            class="ask-send"
            disabled={busy || !question.trim()}
            aria-label="Ask"><SendHorizontal size={22} /></button
          >
        </div>
      </form>
    </section>

    <aside class="ask-side desktop-only">
      {@render spend(false)}
      {#if seen.length > 0}
        <div class="card stack tight">
          <h2 class="form-heading">Looked at</h2>
          {@render sourceList(seen)}
        </div>
      {/if}
      <div class="card stack tight">
        <h2 class="form-heading">Try asking</h2>
        {#each suggestions as suggestion (suggestion)}
          <button
            class="link-button ask-suggestion"
            onclick={() => ask(suggestion)}>{suggestion}</button
          >
        {/each}
      </div>
      <div class="card stack tight">
        <h2 class="form-heading">What it's sent</h2>
        <p class="muted">
          Plants, places, trays, work, losses, treatments, climate, notes as
          they were typed, and the photos you add, to the model the owner chose
          through OpenRouter, asking for providers that keep nothing. Never
          customers, money or who recorded the work. Hortiplanner's server keeps
          what each question cost, who asked and when, not what was asked.
        </p>
      </div>
    </aside>
  </div>
</main>
