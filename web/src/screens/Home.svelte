<script lang="ts">
  import { onMount, tick } from "svelte";
  import { api } from "../api/client";
  import type { Me } from "../lib/boot";
  import type { Schemas } from "../lib/types";
  import {
    describeRecent,
    outcomeProblem,
    repeatOf,
    submit,
    work,
  } from "../lib/work";
  import Manage from "./Manage.svelte";
  import ContainerView from "./work/ContainerView.svelte";
  import StartBatch from "./work/StartBatch.svelte";

  let {
    me,
    atBench,
    onSignedOut,
  }: { me: Me; atBench: boolean; onSignedOut: () => void } = $props();

  const benchIdleLimit = 5 * 60 * 1000;

  type View =
    | { name: "home" }
    | { name: "container"; label: string }
    | { name: "start" }
    | { name: "manage" };

  let view = $state<View>({ name: "home" });
  let label = $state("");
  let scanBox = $state<HTMLInputElement | null>(null);
  let recent = $state<Schemas["RecentAction"][]>([]);
  let done = $state<{ message: string; eventId: string } | null>(null);
  let problem = $state("");

  async function loadRecent() {
    try {
      recent = (await api.GET("/api/events/recent")).data ?? [];
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    }
  }

  // A USB scanner types into the focused box, so it must always be ready.
  async function goHome() {
    view = { name: "home" };
    label = "";
    await loadRecent();
    await tick();
    scanBox?.focus();
  }

  function finishWork(message: string, eventId: string) {
    done = { message, eventId };
    problem = "";
    void goHome();
  }

  function scan(event: SubmitEvent) {
    event.preventDefault();
    const scanned = label.trim();

    if (scanned) {
      done = null;
      problem = "";
      view = { name: "container", label: scanned };
    }
  }

  async function repeat(action: Schemas["RecentAction"]) {
    const item = repeatOf(action);

    if (!item) {
      return;
    }

    const outcome = await submit(item);

    if (outcome.kind === "stored") {
      const suffix = outcome.labelCode ? `. Label: ${outcome.labelCode}` : "";
      finishWork(`Again: ${describeRecent(action)}${suffix}`, outcome.eventId);
    } else {
      done = null;
      problem = outcomeProblem(outcome);
    }
  }

  async function undo(eventId: string) {
    const outcome = await submit(work.undo(eventId));

    if (outcome.kind === "stored") {
      done = null;
      problem = "";
      await loadRecent();
    } else {
      problem = outcomeProblem(outcome);
    }
  }

  async function signOut() {
    await api.POST("/api/account/sign-out");
    onSignedOut();
  }

  onMount(() => {
    void goHome();

    if (!atBench) {
      return;
    }

    // Requests would keep the session alive, so idleness is judged here on the device.
    let timer = setTimeout(signOut, benchIdleLimit);
    const activity = () => {
      clearTimeout(timer);
      timer = setTimeout(signOut, benchIdleLimit);
    };

    window.addEventListener("pointerdown", activity);
    window.addEventListener("keydown", activity);

    return () => {
      clearTimeout(timer);
      window.removeEventListener("pointerdown", activity);
      window.removeEventListener("keydown", activity);
    };
  });
</script>

{#if view.name === "manage"}
  <Manage onBack={goHome} onBecameStation={onSignedOut} />
{:else if view.name === "start"}
  <StartBatch onDone={finishWork} onCancel={goHome} />
{:else if view.name === "container"}
  {#key view.label}
    <ContainerView
      label={view.label}
      onDone={finishWork}
      onCancel={goHome}
      onNotFound={(message) => {
        problem = message;
        void goHome();
      }}
    />
  {/key}
{:else}
  <main>
    <h1>Hello, {me.name}</h1>

    {#if done}
      <div class="card done" role="status">
        <strong>{done.message}</strong>
        <button class="secondary" onclick={() => undo(done!.eventId)}
          >Undo</button
        >
      </div>
    {/if}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <form class="stack" onsubmit={scan}>
      <label>
        Scan a label
        <input
          bind:this={scanBox}
          bind:value={label}
          autocomplete="off"
          autocapitalize="characters"
        />
      </label>
      <button type="submit">Open</button>
    </form>

    <button onclick={() => (view = { name: "start" })}>New batch</button>

    {#if recent.length > 0}
      <h2>Recent</h2>
      {#each recent as action (action.id)}
        <div class="card">
          <span>{describeRecent(action)}</span>
          <div class="row">
            {#if repeatOf(action)}
              <button class="secondary" onclick={() => repeat(action)}
                >Again</button
              >
            {/if}
          </div>
        </div>
      {/each}
    {/if}

    {#if me.isOwner}
      <button class="secondary" onclick={() => (view = { name: "manage" })}
        >Manage</button
      >
    {/if}
    <button class="secondary" onclick={signOut}>Finished</button>
  </main>
{/if}
