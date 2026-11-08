<script lang="ts">
  import { onMount, tick } from "svelte";
  import { api } from "../api/client";
  import type { Me } from "../lib/boot";
  import { rememberContainers, rememberPlaces } from "../lib/offline";
  import { flush, recordWork, useQueue, type FailedWork } from "../lib/queue";
  import type { Schemas } from "../lib/types";
  import {
    describeRecent,
    isRecorded,
    outcomeProblem,
    repeatOf,
    submit,
    work,
  } from "../lib/work";
  import Manage from "./Manage.svelte";
  import Camera from "./work/Camera.svelte";
  import ContainerView from "./work/ContainerView.svelte";
  import StartBatch from "./work/StartBatch.svelte";

  let {
    me,
    atBench,
    onSignedOut,
  }: { me: Me; atBench: boolean; onSignedOut: () => void } = $props();

  const benchIdleLimit = 5 * 60 * 1000;
  const sendEvery = 30 * 1000;
  const cameraAvailable = !!navigator.mediaDevices?.getUserMedia;

  type View =
    | { name: "home" }
    | { name: "container"; label: string }
    | { name: "start" }
    | { name: "camera" }
    | { name: "manage" };

  // svelte-ignore state_referenced_locally
  const queue = useQueue(me.name);

  let view = $state<View>({ name: "home" });
  let label = $state("");
  let scanBox = $state<HTMLInputElement | null>(null);
  let recent = $state<Schemas["RecentAction"][]>([]);
  let done = $state<{
    message: string;
    eventId: string;
    newLabel?: string;
  } | null>(null);
  let problem = $state("");
  let offline = $state(false);
  let waiting = $state(queue.waiting);
  let failed = $state<FailedWork[]>(queue.failed);
  let sending = false;

  function showQueue() {
    waiting = queue.waiting;
    failed = queue.failed;
  }

  // Refreshes what the device keeps for working with no signal.
  async function refresh() {
    try {
      recent = (await api.GET("/api/events/recent")).data ?? [];
      const containers = (await api.GET("/api/containers/active")).data;
      const places = (await api.GET("/api/locations")).data;

      if (containers) {
        rememberContainers(containers);
      }

      if (places) {
        rememberPlaces(places);
      }

      offline = false;
    } catch {
      offline = true;
    }
  }

  async function sendWaiting() {
    if (sending) {
      return;
    }

    sending = true;

    try {
      const before = queue.waiting;
      await flush(queue, submit, outcomeProblem);
      showQueue();

      if (queue.waiting < before) {
        await refresh();
      }
    } finally {
      sending = false;
    }
  }

  // A USB scanner types into the focused box, so it must always be ready.
  async function goHome() {
    view = { name: "home" };
    label = "";
    showQueue();
    await refresh();
    await tick();
    scanBox?.focus();
    void sendWaiting();
  }

  function finishWork(message: string, eventId: string, newLabel?: string) {
    const saved = queue.has(eventId);

    if (saved) {
      queue.describe(eventId, message);
    }

    done = {
      message: saved ? `${message}. Saved on this device for now.` : message,
      eventId,
      newLabel,
    };
    problem = "";
    void goHome();
  }

  function open(code: string) {
    const scanned = code.trim();

    if (scanned) {
      done = null;
      problem = "";
      view = { name: "container", label: scanned };
    }
  }

  function scan(event: SubmitEvent) {
    event.preventDefault();
    open(label);
  }

  async function repeat(action: Schemas["RecentAction"]) {
    const item = repeatOf(action);

    if (!item) {
      return;
    }

    const outcome = await (item.url === "/api/batches"
      ? submit(item)
      : recordWork(item));

    if (isRecorded(outcome)) {
      const suffix =
        outcome.kind === "stored" && outcome.labelCode
          ? `. Label: ${outcome.labelCode}`
          : "";
      finishWork(
        `Again: ${describeRecent(action)}${suffix}`,
        outcome.eventId,
        outcome.kind === "stored" ? outcome.labelCode : undefined,
      );
    } else {
      done = null;
      problem = outcomeProblem(outcome);
    }
  }

  async function undo(eventId: string) {
    if (queue.has(eventId)) {
      queue.remove(eventId);
      done = null;
      showQueue();
      return;
    }

    const outcome = await recordWork(work.undo(eventId));

    if (isRecorded(outcome)) {
      done = null;
      problem = "";
      showQueue();
      await refresh();
    } else {
      problem = outcomeProblem(outcome);
    }
  }

  function dismiss(eventId: string) {
    queue.dismiss(eventId);
    showQueue();
  }

  async function signOut() {
    await api.POST("/api/account/sign-out");
    onSignedOut();
  }

  onMount(() => {
    void goHome();

    const send = () => void sendWaiting();
    const timer = setInterval(send, sendEvery);
    window.addEventListener("online", send);

    const stopSending = () => {
      clearInterval(timer);
      window.removeEventListener("online", send);
    };

    if (!atBench) {
      return stopSending;
    }

    // Requests would keep the session alive, so idleness is judged here on the device.
    let idle = setTimeout(signOut, benchIdleLimit);
    const activity = () => {
      clearTimeout(idle);
      idle = setTimeout(signOut, benchIdleLimit);
    };

    window.addEventListener("pointerdown", activity);
    window.addEventListener("keydown", activity);

    return () => {
      stopSending();
      clearTimeout(idle);
      window.removeEventListener("pointerdown", activity);
      window.removeEventListener("keydown", activity);
    };
  });
</script>

{#if view.name === "manage"}
  <Manage onBack={goHome} onBecameStation={onSignedOut} />
{:else if view.name === "start"}
  <StartBatch onDone={finishWork} onCancel={goHome} />
{:else if view.name === "camera"}
  <Camera onScan={open} onCancel={goHome} />
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

    {#if offline}
      <p class="card" data-testid="offline">
        No connection. Work you record is saved on this device.
      </p>
    {/if}

    {#if waiting > 0}
      <p class="card" data-testid="waiting">
        {waiting}
        {waiting === 1 ? "thing" : "things"} waiting to send. Keep GrowLog open when
        you're back in signal.
      </p>
    {/if}

    {#each failed as item (item.eventId)}
      <div class="card" role="alert">
        <strong>Couldn't be saved: {item.description}</strong>
        <span>{item.reason}</span>
        <button class="secondary" onclick={() => dismiss(item.eventId)}
          >OK</button
        >
      </div>
    {/each}

    {#if done}
      <div class="card done" role="status">
        <strong>{done.message}</strong>
        {#if done.newLabel}
          <a
            class="button"
            href="/api/labels/containers/{encodeURIComponent(done.newLabel)}"
            target="_blank"
            rel="noopener">Print label</a
          >
        {/if}
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

    {#if cameraAvailable}
      <button onclick={() => (view = { name: "camera" })}>Use camera</button>
    {/if}

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
