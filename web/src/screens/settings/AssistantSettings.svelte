<script lang="ts">
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dollars, loadAssistant } from "../../lib/assistant.svelte";
  import { attempt, type Schemas } from "../../lib/types";

  // Ask Hortiplanner's set-up: the owner's OpenRouter key, kept encrypted on the server and never shown again, the model it
  // uses, and who can ask.
  let { onBack }: { onBack: () => void } = $props();

  type Model = Schemas["ModelChoice"];

  let settings = $state<Schemas["AssistantSettings"] | null>(null);
  let models = $state<Model[]>([]);
  let key = $state("");
  let model = $state("");
  let forStaff = $state(false);
  let photosOnly = $state(false);
  let filter = $state("");
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    settings = (await api.GET("/api/assistant/settings")).data ?? null;
    model = settings?.model ?? "";
    forStaff = !!settings?.forStaff;
  }

  async function loadModels() {
    const { data } = await api.GET("/api/assistant/settings/models");
    models = data ?? [];

    if (!data) {
      problem =
        "OpenRouter's list of models couldn't be fetched. Try again later.";
    }
  }

  onMount(
    () =>
      void attempt(async () => {
        await load();
        await loadModels();
      }, setProblem),
  );

  const shown = $derived(
    models.filter(
      (item) =>
        (!photosOnly || item.readsImages) &&
        (!filter.trim() ||
          `${item.name} ${item.id}`
            .toLowerCase()
            .includes(filter.trim().toLowerCase())),
    ),
  );
  const chosen = $derived(models.find((item) => item.id === model));

  // A typical question with a couple of lookups is about 6,000 tokens in and 500 out.
  const perQuestion = (item: Model) =>
    (Number(item.promptPerMillion) * 6000 +
      Number(item.completionPerMillion) * 500) /
    1_000_000;

  const price = (item: Model) => {
    const each = perQuestion(item);
    return each === 0
      ? "free"
      : each < 0.001
        ? "under 0.1¢ a question"
        : `about ${(each * 100).toFixed(1)}¢ a question`;
  };

  const save = (event: SubmitEvent | null, takeKeyAway = false) => {
    event?.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/assistant/settings", {
        body: {
          key: takeKeyAway ? null : key.trim() || null,
          model: model || null,
          forStaff,
          removeKey: takeKeyAway,
        },
      });
      problem = response.ok
        ? ""
        : "That key doesn't look right. OpenRouter keys start sk-or-.";
      saved = response.ok
        ? takeKeyAway
          ? "The key has been taken away, so Ask Hortiplanner is off until you add another."
          : "Saved."
        : "";
      key = "";
      await load();
      await loadAssistant();
    }, setProblem);
  };
</script>

<main>
  <h1>AI assistant</h1>
  <p class="muted">
    Ask Hortiplanner answers questions about plants and the nursery's records,
    and looks at photos of plants, using an AI model you choose through
    OpenRouter. You pay OpenRouter for what it uses; Hortiplanner adds nothing.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <form class="split" onsubmit={(event) => save(event)}>
    <section class="stack">
      <div class="form-card stack">
        <h2 class="form-heading">OpenRouter key</h2>
        <p class={settings?.hasKey ? "backup-ok" : "muted"} role="status">
          {settings?.hasKey ? "A key is saved." : "No key has been added yet."}
          {#if settings?.spent != null}
            It has spent ${Number(settings.spent).toFixed(2)}{settings.limit !=
            null
              ? ` of its $${Number(settings.limit).toFixed(2)} limit`
              : ", with no limit set"}.
          {/if}
        </p>
        <details>
          <summary>Getting a key</summary>
          <ol>
            <li>
              Sign up at OpenRouter and add some credit.
              <a
                class="small-button"
                href="https://openrouter.ai"
                target="_blank"
                rel="noopener"
                >Go to OpenRouter <ExternalLink
                  size={16}
                  aria-hidden="true"
                /></a
              >
            </li>
            <li>
              Create a key, and give it a monthly limit you're happy with.
            </li>
            <li>Paste it here. It's kept encrypted and never shown again.</li>
          </ol>
        </details>
        <label
          >Key <input
            bind:value={key}
            type="password"
            autocomplete="off"
            placeholder={settings?.hasKey
              ? "Saved. Leave blank to keep it."
              : "sk-or-…"}
          /></label
        >
        {#if settings?.hasKey}
          <button
            type="button"
            class="link-button"
            onclick={() => save(null, true)}
            ><Trash2 size={16} aria-hidden="true" />Take the key away</button
          >
        {/if}
      </div>

      <div class="form-card stack">
        <h2 class="form-heading">Who can ask</h2>
        <label class="tick">
          <input type="checkbox" bind:checked={forStaff} /> Staff can ask too, not
          only you
        </label>
        <p class="muted">
          Each person can ask up to 40 questions an hour. The real cap on
          spending is the limit you set on the key at openrouter.ai. The AI
          never sees customers, money or who recorded the work, though notes are
          sent as typed. Hortiplanner keeps what each question cost, who asked
          and when, but not what was asked.
        </p>
      </div>
      <button type="submit">Save</button>

      <section class="form-card stack" aria-labelledby="spend-heading">
        <h2 class="form-heading" id="spend-heading">Spent by each person</h2>
        {#if settings && settings.people.length > 0}
          <ul class="spend-list">
            {#each settings.people as person, index (index)}
              <li>
                <strong>{person.name}</strong>
                <span
                  >{dollars(Number(person.thisMonth))} this month{Number(
                    person.thisMonthQuestions,
                  ) > 0
                    ? `, ${person.thisMonthQuestions} ${Number(person.thisMonthQuestions) === 1 ? "question" : "questions"}`
                    : ""}</span
                >
                <span class="muted">{dollars(Number(person.inAll))} in all</span
                >
              </li>
            {/each}
          </ul>
        {:else}
          <p class="note-empty muted">Nobody has asked anything yet.</p>
        {/if}
        <p class="muted">
          These are in US dollars, because that's how OpenRouter charges.
          Everyone who can ask sees what their own questions have cost.
        </p>
      </section>
    </section>

    <section class="form-card stack">
      <h2 class="form-heading">Model</h2>
      {#if chosen}
        <p>
          You're using <strong>{chosen.name}</strong>, which costs {price(
            chosen,
          )}{chosen.readsImages
            ? ", and it reads photos."
            : ". It can't read photos."}
        </p>
      {:else if model}
        <p>You're using <strong>{model}</strong>.</p>
      {:else}
        <p class="muted">
          Choose one, then press Save. Every model listed can look things up.
        </p>
      {/if}
      <div class="field-grid">
        <label
          >Find <input
            bind:value={filter}
            placeholder="claude, gemini, gpt…"
          /></label
        >
        <label class="tick"
          ><input type="checkbox" bind:checked={photosOnly} /> Only ones that read
          photos</label
        >
      </div>
      <div class="model-list" role="radiogroup" aria-label="Model">
        {#each shown.slice(0, 60) as item (item.id)}
          <label class="model-choice" class:chosen={item.id === model}>
            <input
              type="radio"
              name="model"
              value={item.id}
              bind:group={model}
            />
            <span class="stack tight">
              <strong>{item.name}</strong>
              <span class="muted"
                >{price(item)}{item.readsImages ? " · reads photos" : ""}</span
              >
            </span>
          </label>
        {:else}
          <p class="muted">No models match that.</p>
        {/each}
      </div>
    </section>
  </form>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
