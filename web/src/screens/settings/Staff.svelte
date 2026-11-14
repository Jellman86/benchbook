<script lang="ts">
  import Pencil from "@lucide/svelte/icons/pencil";
  import Camera from "@lucide/svelte/icons/camera";
  import Music from "@lucide/svelte/icons/music";
  import { shrinkPhoto } from "../../lib/photo";
  import Avatar from "../Avatar.svelte";
  import { isOn } from "../../lib/features.svelte";
  import FormPanel from "../FormPanel.svelte";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";

  let { onBack, onSigningIn }: { onBack: () => void; onSigningIn: () => void } =
    $props();

  type Member = Schemas["StaffMember"];
  type SignInWay = "name" | "password" | "invite";

  let staff = $state<Member[]>([]);
  let name = $state("");
  let way = $state<SignInWay>("invite");
  let level = $state<Schemas["SignInLevel"]>("Personal");
  const allowNameOnly = $derived(level !== "Strict");
  const levels: Record<Schemas["SignInLevel"], string> = {
    Open: "No signing in: devices go straight in as the nursery team.",
    Names: "Everyone signs in by tapping their name.",
    Personal: "Each person signs in the way you set for them here.",
    Strict: "Everyone needs a password, passkey or staff login.",
  };
  const everyoneTaps = $derived(level === "Open" || level === "Names");

  let password = $state("");
  let pin = $state("");
  let problem = $state("");
  let link = $state("");
  let myId = $state("");
  let inviteQr = $state("");
  let copied = $state(false);
  const canShare = typeof navigator.share === "function";

  // Drawn whenever there's a new invitation link.
  $effect(() => {
    const current = link;
    inviteQr = "";
    copied = false;

    if (current) {
      void api
        .POST("/api/staff/qr", { body: { text: current } })
        .then(({ data }) => {
          if (link === current) {
            inviteQr = data?.svg ?? "";
          }
        });
    }
  });

  async function shareLink() {
    try {
      await navigator.share({
        title: "Your Hortiplanner invitation",
        url: link,
      });
    } catch {
      // They closed the share sheet.
    }
  }

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(link);
      copied = true;
    } catch {
      copied = false;
    }
  }
  let changingPinFor = $state<string | null>(null);
  let newPin = $state("");
  let settingPasswordFor = $state<string | null>(null);
  let newPassword = $state("");
  let tuneFor = $state<string | null>(null);
  let savingTune = $state(false);
  const maxTuneBytes = 4 * 1024 * 1024;
  let preview: HTMLAudioElement | null = null;

  const pinProblem = "A PIN must be 4 to 6 digits.";
  const passwordProblem = "A password must be at least 8 characters.";
  const setProblem = (message: string) => (problem = message);

  async function load() {
    staff = (await api.GET("/api/staff")).data ?? [];
    myId = (await api.GET("/api/account/me")).data?.id ?? "";
    level =
      (await api.GET("/api/staff/sign-in-settings")).data?.level ?? "Personal";
  }

  onMount(() => void attempt(load, setProblem));

  function signsInWith(member: Member) {
    if (everyoneTaps && !member.isOwner) {
      return "Taps their name";
    }

    if (member.nameOnly) {
      return allowNameOnly
        ? "Signs in by tapping their name"
        : "Tapping their name is turned off, so give them a password or PIN";
    }

    const ways = [
      member.hasPassword ? "password" : null,
      member.hasPasskey ? "passkey" : null,
      member.hasExternalLogin ? "staff login" : null,
    ].filter((item) => item !== null);

    return ways.length > 0
      ? `Signs in with ${ways.join(" or ")}`
      : member.hasPin
        ? "Bench PIN only"
        : "Can't sign in yet";
  }

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/staff", {
        body: {
          name,
          pin: optional(pin),
          password: way === "password" ? password : null,
          nameOnly: way === "name",
        },
      });

      if (response.status === 409) {
        problem = "Someone with that name is already on the list.";
      } else if (!response.ok) {
        problem = optional(pin) === null ? passwordProblem : pinProblem;
      } else {
        problem = "";
        name = "";
        pin = "";
        password = "";
        await load();
      }
    }, setProblem);
  };

  const invite = (id: string) =>
    attempt(async () => {
      const { data } = await api.POST("/api/staff/{id}/invite", {
        params: { path: { id } },
      });

      if (data) {
        link = `${location.origin}/invite?user=${id}&token=${encodeURIComponent(data.token)}`;
        await tick();
        document
          .querySelector(".invite-card")
          ?.scrollIntoView({ behavior: "smooth", block: "center" });
      }
    }, setProblem);

  const savePin = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/staff/{id}/pin", {
        params: { path: { id } },
        body: { pin: newPin },
      });

      if (response.ok) {
        changingPinFor = null;
        newPin = "";
        await load();
      } else {
        problem = pinProblem;
      }
    }, setProblem);
  };

  const savePassword = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/staff/{id}/password", {
        params: { path: { id } },
        body: { password: newPassword },
      });

      if (response.ok) {
        settingPasswordFor = null;
        newPassword = "";
        await load();
      } else {
        problem = passwordProblem;
      }
    }, setProblem);
  };

  // A sign-in tune: a bit of fun the owner chooses, played on that person's device when they sign in.
  const chooseTune = (event: Event, member: Member) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = "";

    if (!file) {
      return;
    }

    // Checked here too: a phone sending a big file the server then refuses can look like a lost connection.
    if (file.size > maxTuneBytes) {
      problem = "That tune is too big. Use a clip under 4 MB.";
      return;
    }

    savingTune = true;

    return attempt(async () => {
      try {
        const response = await fetch(`/api/staff/${member.id}/tune`, {
          method: "PUT",
          body: file,
        });

        problem = response.ok
          ? ""
          : response.status === 415
            ? "That file can't be played on a phone. Use an MP3, M4A or WAV."
            : response.status === 413
              ? "That tune is too big. Use a clip under 4 MB."
              : "The tune couldn't be saved. Please try again.";
        await load();
      } finally {
        savingTune = false;
      }
    }, setProblem);
  };

  function playPreview(member: Member) {
    preview?.pause();
    preview = new Audio(`/api/staff/${member.id}/tune?at=${Date.now()}`);
    void preview.play().catch(() => {});
  }

  // A picture shown beside their name. Shrunk on the phone first: it's only ever shown small.
  const choosePicture = (event: Event, member: Member) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = "";

    if (!file) {
      return;
    }

    return attempt(async () => {
      const response = await fetch(`/api/staff/${member.id}/picture`, {
        method: "PUT",
        body: await shrinkPhoto(file, 512),
      });
      problem = response.ok
        ? ""
        : "That picture couldn't be saved. Please use a photo.";
      await load();
    }, setProblem);
  };

  const removePicture = (member: Member) =>
    attempt(async () => {
      await api.DELETE("/api/staff/{id}/picture", {
        params: { path: { id: member.id } },
      });
      await load();
    }, setProblem);

  const removeTune = (member: Member) =>
    attempt(async () => {
      preview?.pause();
      await api.DELETE("/api/staff/{id}/tune", {
        params: { path: { id: member.id } },
      });
      await load();
    }, setProblem);

  const makeNameOnly = (member: Member) => {
    if (
      !confirm(
        `${member.name} will sign in by tapping their name, with no password. Anyone on the nursery network could do the same.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/staff/{id}/name-only", {
        params: { path: { id: member.id } },
      });
      await load();
    }, setProblem);
  };

  const welcomeBack = (member: Member) =>
    attempt(async () => {
      await api.POST("/api/staff/{id}/restore", {
        params: { path: { id: member.id } },
      });
      await load();
    }, setProblem);

  const archive = (member: Member) => {
    if (
      !confirm(`${member.name} has left? They'll no longer be able to sign in.`)
    ) {
      return;
    }

    return attempt(async () => {
      const { response } = await api.POST("/api/staff/{id}/archive", {
        params: { path: { id: member.id } },
      });

      if (response.ok) {
        await load();
      } else {
        problem = "You can't remove your own account.";
      }
    }, setProblem);
  };
</script>

<main>
  <h1>Staff</h1>

  <div class="split">
    <section>
      <FormPanel label="Add a person">
        <form class="stack" onsubmit={add}>
          <label>Name <input bind:value={name} required /></label>
          <label>
            How they sign in
            <select bind:value={way}>
              {#if allowNameOnly}
                <option value="name">Just tap their name</option>
              {/if}
              <option value="password">A password you give them</option>
              <option value="invite">Send them an invitation later</option>
            </select>
          </label>
          {#if way === "password"}
            <label>
              Password (at least 8 characters)
              <input
                bind:value={password}
                autocomplete="off"
                minlength="8"
                required
              />
            </label>
          {/if}
          {#if isOn("BenchStations")}
            <label
              >PIN for the bench (4 to 6 digits, optional)
              <input bind:value={pin} inputmode="numeric" autocomplete="off" />
            </label>
          {/if}
          <button type="submit">Add</button>
        </form>
      </FormPanel>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#if link}
        <div class="card invite-card">
          <p>
            Let them scan this with their phone, or send them the link. It works
            once, for 7 days.
          </p>
          {#if inviteQr}
            <img
              class="invite-qr"
              src="data:image/svg+xml;charset=utf-8,{encodeURIComponent(
                inviteQr,
              )}"
              alt="QR code of the invitation link"
            />
          {/if}
          <p class="link" data-testid="invite-link">{link}</p>
          <div class="row">
            {#if canShare}
              <button onclick={shareLink}>Share</button>
            {/if}
            <button class="secondary" onclick={copyLink}
              >{copied ? "Copied" : "Copy link"}</button
            >
          </div>
        </div>
      {/if}

      <p class="muted">
        {levels[level]}
        <button class="link-button" onclick={onSigningIn}
          ><Pencil size={16} aria-hidden="true" />Change under Signing in</button
        >
      </p>

      {#each staff.filter((member) => !member.archived) as member (member.id)}
        <div class="card">
          <strong class="person-heading">
            <Avatar
              id={member.id}
              name={member.name}
              pictureAt={member.pictureAt}
            />
            <span>{member.name}</span>
          </strong>
          <span class="muted">{signsInWith(member)}</span>
          {#if changingPinFor === member.id}
            <form class="stack" onsubmit={(event) => savePin(event, member.id)}>
              <label
                >New PIN
                <input
                  bind:value={newPin}
                  inputmode="numeric"
                  autocomplete="off"
                  required
                />
              </label>
              <button type="submit">Save PIN</button>
            </form>
          {:else if settingPasswordFor === member.id}
            <form
              class="stack"
              onsubmit={(event) => savePassword(event, member.id)}
            >
              <label
                >New password
                <input bind:value={newPassword} autocomplete="off" required />
              </label>
              <button type="submit">Save password</button>
            </form>
          {:else if tuneFor === member.id}
            <div class="stack">
              <h3 class="form-heading">Picture</h3>
              <p class="muted">
                Shown beside their name when signing in, at a bench and here.
                They can change it themselves under My account.
              </p>
              <label class="photo-choice">
                <Camera size={20} />
                <span
                  >{member.pictureAt
                    ? "Change the picture"
                    : "Choose a picture"}</span
                >
                <input
                  type="file"
                  accept="image/*"
                  onchange={(event) => choosePicture(event, member)}
                />
              </label>
              {#if member.pictureAt}
                <button
                  class="secondary destructive"
                  onclick={() => removePicture(member)}
                  >Remove the picture</button
                >
              {/if}
              <h3 class="form-heading">Sign-in tune</h3>
              <p class="muted">
                Plays on {member.name}'s device each time they sign in. They can
                turn it off. An MP3, M4A or WAV, up to 4 MB.
              </p>
              <label class="photo-choice">
                <Music size={20} />
                <span
                  >{member.hasTune ? "Change the tune" : "Choose a tune"}</span
                >
                <input
                  type="file"
                  accept="audio/*"
                  disabled={savingTune}
                  onchange={(event) => chooseTune(event, member)}
                />
              </label>
              {#if savingTune}<p class="muted" role="status">
                  Saving the tune…
                </p>{/if}
              {#if member.hasTune}
                <div class="row">
                  <button class="secondary" onclick={() => playPreview(member)}
                    >Play it</button
                  >
                  <button
                    class="secondary destructive"
                    onclick={() => removeTune(member)}>Remove the tune</button
                  >
                </div>
              {/if}
              <button
                class="link-button"
                onclick={() => {
                  preview?.pause();
                  tuneFor = null;
                }}>Done</button
              >
            </div>
          {:else}
            <div class="row">
              <button class="secondary" onclick={() => invite(member.id)}
                >Phone invitation</button
              >
              {#if isOn("BenchStations")}
                <button
                  class="secondary"
                  onclick={() => (changingPinFor = member.id)}>New PIN</button
                >
              {/if}
              <button
                class="secondary"
                onclick={() => (settingPasswordFor = member.id)}
                >Set password</button
              >
              {#if allowNameOnly && !member.isOwner && !member.nameOnly}
                <button class="secondary" onclick={() => makeNameOnly(member)}
                  >Name only</button
                >
              {/if}
              <button
                class="secondary"
                aria-label={member.hasTune
                  ? "Picture and sign-in tune, a tune is set"
                  : "Picture and sign-in tune"}
                onclick={() => (tuneFor = member.id)}
                >{member.hasTune
                  ? "Picture and tune ♪"
                  : "Picture and tune"}</button
              >
              {#if member.id !== myId}
                <button class="secondary" onclick={() => archive(member)}
                  >Has left</button
                >
              {/if}
            </div>
          {/if}
        </div>
      {/each}

      {#if staff.some((member) => member.archived)}
        <details class="archived">
          <summary>People who have left</summary>
          {#each staff.filter((member) => member.archived) as member (member.id)}
            <div class="card recent">
              <span>{member.name}</span>
              <div class="row">
                <button class="secondary" onclick={() => welcomeBack(member)}
                  >Has come back</button
                >
              </div>
            </div>
          {/each}
        </details>
      {/if}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
