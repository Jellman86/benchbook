<script lang="ts">
  import { stopTune } from "../lib/tune";
  import { loadFeatures, watchFeatures } from "../lib/features.svelte";
  import CircleCheck from "@lucide/svelte/icons/circle-check";
  import { onMount, tick } from "svelte";
  import { focusHeading } from "../lib/steps";
  import { api, sessionEnded } from "../api/client";
  import {
    externalSignInProblem,
    signInAsYourselfKey,
    type Me,
  } from "../lib/boot";
  import {
    rememberBench,
    rememberContainers,
    rememberPlaces,
  } from "../lib/offline";
  import {
    flush,
    recordWork,
    useQueue,
    stopQueue,
    waitingByOthers,
    type FailedWork,
  } from "../lib/queue";
  import type { Schemas } from "../lib/types";
  import {
    describeRecent,
    isRecorded,
    outcomeProblem,
    repeatOf,
    undoProblem,
    type BatchPrefill,
    submit,
    work,
  } from "../lib/work";
  import {
    isManageSection,
    sectionLabel,
    type ManageSection,
  } from "../lib/sections";
  import {
    movedToSettings,
    settingsLabel,
    type SettingsPage,
  } from "../lib/settings";
  import { crumbs, type Crumb } from "../lib/crumbs.svelte";
  import Breadcrumbs from "./Breadcrumbs.svelte";
  import {
    applyTheme,
    nextTheme,
    savedCollapsed,
    savedTheme,
    saveCollapsed,
    type Theme,
  } from "../lib/theme";
  import BottomNav from "./BottomNav.svelte";
  import Manage from "./Manage.svelte";
  import Production from "./Production.svelte";
  import Plants from "./manage/Plants.svelte";
  import Overview from "./Overview.svelte";
  import PrintButton from "./PrintButton.svelte";
  import Settings from "./Settings.svelte";
  import Sidebar from "./Sidebar.svelte";
  import Camera from "./work/Camera.svelte";
  import ContainerView from "./work/ContainerView.svelte";
  import FindTray from "./work/FindTray.svelte";
  import MoveMany from "./work/MoveMany.svelte";
  import LabelSheets from "./work/LabelSheets.svelte";
  import JobsDue from "./work/JobsDue.svelte";
  import PlaceView from "./work/PlaceView.svelte";
  import { findPlaceByCode, isPlaceCode, type Place } from "../lib/places";
  import { rememberedPlaces } from "../lib/offline";
  import StartBatch from "./work/StartBatch.svelte";
  import Ask from "./Ask.svelte";
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import {
    assistant,
    loadAssistant,
    type AskAbout,
  } from "../lib/assistant.svelte";

  let {
    me,
    atBench,
    onSignedOut,
    onMeChanged = () => {},
  }: {
    me: Me;
    atBench: boolean;
    onSignedOut: () => void;
    // The signed-in person changed something about themselves, such as their picture.
    onMeChanged?: (me: Me) => void;
  } = $props();

  const benchIdleLimit = 5 * 60 * 1000;
  const stockRefreshEvery = 5 * 60 * 1000;

  const screenTitles: Record<string, string> = {
    home: "Record work",
    container: "Tray",
    start: "New batch",
    camera: "Scan",
    settings: "Settings",
    production: "Production",
    place: "Place",
    moveMany: "Move several",
    labelSheets: "Labels on A4",
    plants: "Plants",
    manage: "Manage",
    ask: "Ask Hortiplanner",
  };
  const sendEvery = 30 * 1000;
  const cameraAvailable = !!navigator.mediaDevices?.getUserMedia;

  type View =
    | { name: "home" }
    | { name: "container"; label: string; fromProduction?: boolean }
    | { name: "start"; prefill?: BatchPrefill }
    | { name: "camera" }
    | { name: "settings"; page: SettingsPage | null }
    | { name: "production" }
    | { name: "place"; id: string }
    | { name: "moveMany"; destination?: string | null }
    | { name: "labelSheets"; trays?: string[] }
    | { name: "plants" }
    | { name: "ask"; about?: AskAbout | null }
    | {
        name: "manage";
        section: ManageSection | null;
        batchId?: string | null;
      };

  // Bumped to show a screen afresh, from the top, when its breadcrumb is tapped.
  let fresh = $state(0);

  function again(next: View) {
    view = next;
    fresh += 1;
  }

  const recordWorkCrumb: Crumb = {
    label: "Record work",
    go: () => void goHome(),
  };

  // The top of the breadcrumb trail: the screens below add their own part.
  crumbs((): Crumb[] => {
    const current = view;

    switch (current.name) {
      case "home":
        return [{ label: "Record work" }];
      case "container":
        return current.fromProduction
          ? [
              {
                label: "Production",
                go: () => again({ name: "production" }),
              },
            ]
          : [recordWorkCrumb];
      case "start":
        return [recordWorkCrumb, { label: "New batch" }];
      case "camera":
        return [recordWorkCrumb, { label: "Scan" }];
      case "production":
        return [
          { label: "Production", go: () => again({ name: "production" }) },
        ];
      case "place":
        return [recordWorkCrumb];
      case "moveMany":
        return [recordWorkCrumb, { label: "Move several trays" }];
      case "labelSheets":
        return [recordWorkCrumb, { label: "Labels on A4" }];
      case "plants":
        return [{ label: "Plants", go: () => again({ name: "plants" }) }];
      case "ask":
        return [recordWorkCrumb, { label: "Ask Hortiplanner" }];
      case "settings":
        return [
          {
            label: "Settings",
            go: () => again({ name: "settings", page: null }),
          },
          ...(current.page ? [{ label: settingsLabel(current.page) }] : []),
        ];
      case "manage": {
        const section = current.section;
        return [
          {
            label: "Manage",
            go: () => again({ name: "manage", section: null }),
          },
          ...(section
            ? [
                {
                  label: sectionLabel(section),
                  go: () => again({ name: "manage", section }),
                },
              ]
            : []),
        ];
      }
    }
  });

  // svelte-ignore state_referenced_locally
  const queue = useQueue(me.id, me.name);
  let queueProblem = "";
  // Work used to be kept under a person's name, which can change.
  try {
    // svelte-ignore state_referenced_locally
    queue.adopt(me.name);
  } catch {
    queueProblem =
      "This device could not update its waiting work. Check its storage and try again.";
  }

  // A reload comes back to the screen it was on, including one kept before Settings had its own section.
  function upgrade(kept: unknown): View | undefined {
    const old = kept as
      { name?: string; section?: unknown; page?: unknown } | undefined;

    if (old?.name === "more") {
      return { name: "settings", page: "account" };
    }

    if (old?.name === "manage" && old.section != null) {
      const page = movedToSettings(old.section);

      if (page) {
        return { name: "settings", page };
      }

      if (!isManageSection(old.section)) {
        return { name: "manage", section: null };
      }
    }

    return kept as View | undefined;
  }

  const keptView = upgrade((history.state as { view?: unknown } | null)?.view);
  let view = $state<View>(keptView ?? { name: "home" });
  let notice = $state("");
  let lastStockRefresh = 0;
  let label = $state("");
  let scanBox = $state<HTMLInputElement | null>(null);
  let recent = $state<Schemas["RecentAction"][]>([]);
  let done = $state<{
    message: string;
    eventId: string;
    newLabel?: string;
  } | null>(null);
  let problem = $state(queueProblem);
  let offline = $state(false);
  let waiting = $state(queue.waiting);
  let failed = $state<FailedWork[]>(queue.failed);
  // svelte-ignore state_referenced_locally
  const othersWaiting = waitingByOthers(me.id);
  let backupNeverReported = $state(false);
  let backupStopped = $state(false);
  let collapsed = $state(savedCollapsed());
  let theme = $state<Theme>(savedTheme());
  let sending = false;
  let fromHistory = false;

  // Each screen is a step in the browser's history, so the phone's Back button goes back a screen.
  $effect(() => {
    const current = $state.snapshot(view);
    const kept = (history.state as { view?: View } | null)?.view;

    if (fromHistory) {
      fromHistory = false;
    } else if (JSON.stringify(kept) !== JSON.stringify(current)) {
      if (kept) {
        history.pushState({ view: current }, "");
      } else {
        history.replaceState({ view: current }, "");
      }
    }

    document.title = `${screenTitles[current.name]} · Hortiplanner`;

    // Someone using a screen reader or keyboard starts at the top of the new screen.
    if (current.name !== "home") {
      void focusHeading();
    }
  });

  function setTheme(chosen: Theme) {
    theme = chosen;
    applyTheme(chosen);
  }

  function toggleCollapsed() {
    collapsed = !collapsed;
    saveCollapsed(collapsed);
  }

  function showQueue() {
    waiting = queue.waiting;
    failed = queue.failed;
  }

  // Refreshes what the device keeps for working with no signal.
  async function refresh() {
    try {
      recent = (await api.GET("/api/events/recent")).data ?? [];

      if (me.isOwner) {
        const backups = (await api.GET("/api/backups/status")).data;
        backupNeverReported = !!backups && !backups.known;
        backupStopped = !!backups && !backups.healthy;
      }
      // What the device keeps for no signal is refreshed every few minutes, not after every press.
      if (Date.now() - lastStockRefresh > stockRefreshEvery) {
        const containers = (await api.GET("/api/containers/active")).data;
        const places = (await api.GET("/api/locations")).data;

        if (containers) {
          rememberContainers(containers);
        }

        if (places) {
          rememberPlaces(places);
        }

        lastStockRefresh = Date.now();
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

      // Once it has gone, the saved card stops saying it's only on this device.
      if (done && !queue.has(done.eventId)) {
        done = {
          ...done,
          message: done.message.replace(". Saved on this device for now.", ""),
        };
      }

      if (queue.waiting < before) {
        await refresh();
      }
    } catch {
      problem =
        "This device could not update its waiting work. Keep Hortiplanner open and try again.";
    } finally {
      sending = false;
    }
  }

  const trace = (batchId: string | null) =>
    (view = { name: "manage", section: "trace", batchId });

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
    notice = "";
    const saved = queue.has(eventId);

    if (saved) {
      updateQueue(() => queue.describe(eventId, message));
    }

    done = {
      message: saved ? `${message}. Saved on this device for now.` : message,
      eventId,
      newLabel,
    };
    problem = "";
    void goHome();
  }

  async function openPlace(code: string) {
    let places: Place[];

    try {
      places = (await api.GET("/api/locations")).data ?? [];
    } catch {
      places = rememberedPlaces() as Place[];
    }

    const place = findPlaceByCode(code, places);

    if (place) {
      done = null;
      problem = "";
      view = { name: "place", id: place.id };
    } else {
      problem = `No place has the label ${code.trim()}.`;
    }
  }

  function open(code: string, fromProduction = false) {
    const scanned = code.trim();

    if (isPlaceCode(scanned)) {
      void openPlace(scanned);
    } else if (scanned) {
      done = null;
      notice = "";
      problem = "";
      view = { name: "container", label: scanned, fromProduction };
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

  async function undo(eventId: string, words: string) {
    notice = "";

    if (queue.has(eventId)) {
      if (!updateQueue(() => queue.remove(eventId))) return;
      done = null;
      notice = `Undone: ${words}`;
      showQueue();
      return;
    }

    const outcome = await recordWork(work.undo(eventId));

    if (isRecorded(outcome)) {
      done = null;
      problem = "";
      notice = `Undone: ${words}`;
      showQueue();
      await refresh();
    } else {
      problem = undoProblem(outcome);
    }
  }

  function undoRecent(action: Schemas["RecentAction"]) {
    if (
      confirm(
        `Undo "${describeRecent(action)}"? It stays in the tray's history, marked as undone.`,
      )
    ) {
      void undo(action.id, describeRecent(action));
    }
  }

  function updateQueue(change: () => void): boolean {
    try {
      change();
      showQueue();
      return true;
    } catch {
      problem =
        "This device could not update its waiting work. Check its storage and try again.";
      return false;
    }
  }

  function dismiss(eventId: string) {
    updateQueue(() => queue.dismiss(eventId));
  }

  function retry(eventId: string) {
    if (updateQueue(() => queue.retry(eventId))) void sendWaiting();
  }

  async function signOut() {
    stopTune();
    if (
      queue.waiting > 0 &&
      !confirm(
        `${queue.waiting} ${queue.waiting === 1 ? "thing you recorded is" : "things you recorded are"} still waiting to send. They'll send next time you sign in on this device. Sign out anyway?`,
      )
    ) {
      return;
    }

    // Leaving the nursery team account means someone wants to sign in as themselves.
    if (me.isTeam) {
      try {
        sessionStorage.setItem(signInAsYourselfKey, "yes");
      } catch {
        // Without storage the device simply goes back in as the team.
      }
    }

    stopQueue();
    await api.POST("/api/account/sign-out");
    onSignedOut();
  }

  // A change the owner makes to Settings, Features reaches this device when the app comes back into view.
  onMount(watchFeatures);

  // Whether Ask Hortiplanner can be offered, checked again whenever the app comes back into view.
  onMount(() => {
    const visible = () => {
      if (document.visibilityState === "visible") {
        void loadAssistant();
      }
    };

    document.addEventListener("visibilitychange", visible);
    return () => document.removeEventListener("visibilitychange", visible);
  });

  onMount(() => {
    void loadFeatures().then(loadAssistant);

    if (location.search.includes("sso=")) {
      problem = externalSignInProblem(location.search);
      history.replaceState(null, "", location.pathname);
    }

    rememberBench(atBench);

    if (view.name === "home") {
      void goHome();
    } else {
      void refresh();
    }

    const stepBack = (event: PopStateEvent) => {
      fromHistory = true;
      view = upgrade((event.state as { view?: unknown } | null)?.view) ?? {
        name: "home",
      };
    };
    window.addEventListener("popstate", stepBack);

    const send = () => void sendWaiting();
    const timer = setInterval(send, sendEvery);
    window.addEventListener("online", send);

    window.addEventListener(sessionEnded, stopQueue);
    const stopSending = () => {
      stopQueue();
      window.removeEventListener(sessionEnded, stopQueue);
      clearInterval(timer);
      window.removeEventListener("online", send);
      window.removeEventListener("popstate", stepBack);
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

  // Ask Hortiplanner about something on screen, when this person can ask and isn't at a shared bench.
  const askAbout = $derived(
    assistant.canAsk && !atBench
      ? (about: AskAbout) => (view = { name: "ask", about })
      : undefined,
  );
</script>

<!-- Ask Hortiplanner about what's on screen, wherever it can be asked: one way in for trays, plants and photos. -->
{#snippet screen()}
  {#if view.name === "manage"}
    <Manage
      section={view.section}
      batchId={view.batchId}
      onSection={(section) => (view = { name: "manage", section })}
      onTrace={trace}
      onBack={goHome}
      onStartBatch={(prefill) => (view = { name: "start", prefill })}
      onOpenTray={(code) => open(code)}
      onMoveTraysHere={(id) => (view = { name: "moveMany", destination: id })}
      onAsk={askAbout}
    />
  {:else if view.name === "start"}
    <StartBatch prefill={view.prefill} onDone={finishWork} onCancel={goHome} />
  {:else if view.name === "camera"}
    <Camera onScan={open} onCancel={goHome} />
  {:else if view.name === "place"}
    {#key view.id}
      <PlaceView
        placeId={view.id}
        canManage={me.isOwner}
        onBack={goHome}
        onOpenTray={(code) => open(code)}
        onOpenPlace={(id) => (view = { name: "place", id })}
        onMoveTraysHere={(id) => (view = { name: "moveMany", destination: id })}
      />
    {/key}
  {:else if view.name === "moveMany"}
    <MoveMany destination={view.destination} onFinished={goHome} />
  {:else if view.name === "labelSheets"}
    <LabelSheets onBack={goHome} trayLabels={view.trays ?? []} />
  {:else if view.name === "plants"}
    <Plants canManage={false} onBack={goHome} onAsk={askAbout} />
  {:else if view.name === "ask"}
    <Ask
      {me}
      about={view.about ?? null}
      onOpenTray={(code) => open(code)}
      onOpenPlace={(id) => (view = { name: "place", id })}
    />
  {:else if view.name === "production"}
    <Production
      onOpen={(code) => open(code, true)}
      onBack={goHome}
      onLabels={(trays) => (view = { name: "labelSheets", trays })}
    />
  {:else if view.name === "settings"}
    <Settings
      {me}
      onPictureChanged={(pictureAt) => onMeChanged({ ...me, pictureAt })}
      page={view.page}
      {theme}
      onPage={(page) => (view = { name: "settings", page })}
      onTheme={setTheme}
      onBack={goHome}
      onSignOut={signOut}
      onBecameStation={onSignedOut}
    />
  {:else if view.name === "container"}
    {#key view.label}
      <ContainerView
        label={view.label}
        onAsk={askAbout}
        onTrace={me.isOwner && !atBench ? trace : undefined}
        onDone={finishWork}
        onCancel={view.fromProduction
          ? () => (view = { name: "production" })
          : goHome}
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
        <p class="card notice" data-testid="offline">
          There's no connection, so the work you record is being saved on this
          device.
        </p>
      {/if}

      {#if backupStopped}
        <div class="card problem" role="alert">
          <span
            >{backupNeverReported
              ? "Hortiplanner has never heard from the backups. Please check that the nursery's records are being backed up."
              : "The backups may have stopped. The nursery's records aren't being protected until this is fixed."}</span
          >
          <button
            class="secondary"
            onclick={() => (view = { name: "settings", page: "data" })}
            >See backups</button
          >
        </div>
      {/if}

      {#if waiting > 0}
        <p class="card notice" data-testid="waiting">
          {waiting}
          {waiting === 1 ? "thing is" : "things are"} waiting to send. Keep Hortiplanner
          open once you're back in signal and {waiting === 1 ? "it" : "they"}'ll
          go.
        </p>
      {/if}

      {#each failed as item (item.eventId)}
        <div class="card problem" role="alert">
          <strong>This couldn't be saved: {item.description}</strong>
          <span>{item.reason}</span>
          <div class="row">
            {#if item.request}
              <button onclick={() => retry(item.eventId)}>Try again</button>
            {/if}
            <button class="secondary" onclick={() => dismiss(item.eventId)}
              >Forget it</button
            >
          </div>
        </div>
      {/each}

      {#if done}
        <div class="card done" role="status">
          <strong class="done-heading"
            ><CircleCheck size={22} />{done.message}</strong
          >
          {#if done.newLabel}
            <PrintButton
              href="/api/labels/containers/{encodeURIComponent(done.newLabel)}"
              label="Print label"
              onPrinted={() => scanBox?.focus()}
            />
          {/if}
          <button
            class="secondary"
            onclick={() => undo(done!.eventId, done!.message)}>Undo</button
          >
        </div>
      {/if}
      {#if notice}<p class="card notice" role="status">{notice}</p>{/if}
      {#each othersWaiting as other (other.owner)}
        <p class="card notice">
          {other.count}
          {other.count === 1 ? "thing" : "things"} recorded by {other.name}
          {other.count === 1 ? "is" : "are"} waiting on this device. They'll send
          when {other.name} signs in here.
        </p>
      {/each}
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      <div class="split">
        <section>
          {#if assistant.canAsk && !atBench}
            <button class="ask-card" onclick={() => (view = { name: "ask" })}>
              <Sparkles size={22} aria-hidden="true" />
              <span class="stack tight">
                <strong>Ask Hortiplanner</strong>
                <span class="muted"
                  >Ask about plants, the trays, losses and photos of plants.</span
                >
              </span>
            </button>
          {/if}

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
            <button type="submit">Open this label</button>
          </form>

          <FindTray onOpen={(code) => open(code)} />

          {#if cameraAvailable}
            <button
              class="secondary"
              onclick={() => (view = { name: "camera" })}>Use camera</button
            >
          {/if}
          <button
            class="secondary"
            onclick={() => (view = { name: "moveMany" })}
            >Move several trays</button
          >
          {#if !atBench}
            <button
              class="secondary"
              onclick={() => (view = { name: "labelSheets" })}
              >Print labels on A4</button
            >
          {/if}

          {#if me.isOwner && !atBench}
            <div class="desktop-only">
              <Overview onOpen={(code) => open(code)} />
            </div>
          {/if}

          {#if atBench}
            <button onclick={() => (view = { name: "start" })}>New batch</button
            >
            <button
              class="secondary"
              onclick={() => (view = { name: "production" })}>Production</button
            >
            <button
              class="secondary"
              onclick={() => (view = { name: "plants" })}>Plants</button
            >
          {/if}
        </section>

        <section>
          {#if !atBench}
            <JobsDue
              isOwner={me.isOwner}
              onGo={(where) =>
                (view =
                  where === "nursery"
                    ? { name: "settings", page: "nursery" }
                    : where === "production"
                      ? { name: "production" }
                      : {
                          name: "manage",
                          section: where === "jobs" ? "jobs" : "invoices",
                        })}
            />
          {/if}
          {#if recent.length > 0}
            <h2>Recent</h2>
            {#each recent as action (action.id)}
              <div class="card recent">
                <span>{describeRecent(action)}</span>
                <div class="row">
                  {#if repeatOf(action)}
                    <button class="secondary" onclick={() => repeat(action)}
                      >Do it again</button
                    >
                  {/if}
                  {#if action.type !== "Sold" && !(action.details as { forPassport?: boolean } | null)?.forPassport}
                    <button class="secondary" onclick={() => undoRecent(action)}
                      >Undo</button
                    >
                  {/if}
                </div>
              </div>
            {/each}
          {:else}
            <p class="muted desktop-only">
              Nothing has been recorded yet. Scan a label, find a tray, or start
              a new batch.
            </p>
          {/if}
        </section>
      </div>

      {#if atBench}
        <button class="secondary" onclick={signOut}>Finished</button>
      {/if}
    </main>
  {/if}
{/snippet}

{#if atBench}
  <div class="bench-layout">
    <Breadcrumbs />
    {#key fresh}{@render screen()}{/key}
  </div>
{:else}
  <div class="with-sidebar" class:collapsed>
    <Sidebar
      {me}
      {collapsed}
      {theme}
      current={view.name === "manage"
        ? view.section
        : view.name === "container"
          ? view.fromProduction
            ? "production"
            : "home"
          : view.name === "home" ||
              view.name === "ask" ||
              view.name === "start" ||
              view.name === "settings" ||
              view.name === "plants" ||
              view.name === "production"
            ? view.name
            : null}
      onHome={goHome}
      onNewBatch={() => (view = { name: "start" })}
      onAsk={assistant.canAsk ? () => (view = { name: "ask" }) : undefined}
      onSection={(section) => (view = { name: "manage", section })}
      onToggleCollapsed={toggleCollapsed}
      onCycleTheme={() => setTheme(nextTheme(theme))}
      onSettings={() => (view = { name: "settings", page: null })}
      onProduction={() => (view = { name: "production" })}
      onPlants={() => (view = { name: "plants" })}
      onFinished={signOut}
    />
    <div class="content">
      <div class="topbar">
        <img src="/logo-96.png" alt="" width="32" height="32" />
        Hortiplanner
      </div>
      <Breadcrumbs />
      {#key fresh}{@render screen()}{/key}
    </div>
    <BottomNav
      {me}
      current={view.name === "home" ||
      view.name === "start" ||
      view.name === "manage" ||
      view.name === "settings" ||
      view.name === "plants" ||
      view.name === "production"
        ? view.name
        : null}
      onHome={goHome}
      onNewBatch={() => (view = { name: "start" })}
      onManage={() => (view = { name: "manage", section: null })}
      onProduction={() => (view = { name: "production" })}
      onPlants={() => (view = { name: "plants" })}
      onSettings={() => (view = { name: "settings", page: null })}
    />
  </div>
{/if}
