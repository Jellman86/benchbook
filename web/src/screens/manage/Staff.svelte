<script lang="ts">
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  type Member = Schemas["StaffMember"];
  type SignInWay = "name" | "password" | "invite";

  let staff = $state<Member[]>([]);
  let name = $state("");
  let way = $state<SignInWay>("invite");
  let allowNameOnly = $state(false);
  let password = $state("");
  let pin = $state("");
  let problem = $state("");
  let link = $state("");
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
      await navigator.share({ title: "Your GrowLog invitation", url: link });
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

  const pinProblem = "A PIN must be 4 to 6 digits.";
  const passwordProblem = "A password must be at least 8 characters.";
  const setProblem = (message: string) => (problem = message);

  async function load() {
    staff = (await api.GET("/api/staff")).data ?? [];
    allowNameOnly =
      (await api.GET("/api/staff/sign-in-settings")).data?.allowNameOnly ??
      false;
  }

  const setAllowNameOnly = (allow: boolean) => {
    if (
      allow &&
      !confirm(
        "Let staff sign in just by tapping their name? Anyone who can reach GrowLog could sign in as them, so only do this if it is on the nursery's private network.",
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.PUT("/api/staff/sign-in-settings", {
        body: { allowNameOnly: allow },
      });
      await load();
    }, setProblem);
  };

  onMount(() => void attempt(load, setProblem));

  function signsInWith(member: Member) {
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
          <label
            >PIN for the bench (4 to 6 digits, optional)
            <input bind:value={pin} inputmode="numeric" autocomplete="off" />
          </label>
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

      <label class="tick">
        <input
          type="checkbox"
          checked={allowNameOnly}
          onchange={(event) => setAllowNameOnly(event.currentTarget.checked)}
        />
        Let staff sign in by tapping their name
      </label>

      {#each staff.filter((member) => !member.archived) as member (member.id)}
        <div class="card">
          <strong>{member.name}</strong>
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
          {:else}
            <div class="row">
              <button class="secondary" onclick={() => invite(member.id)}
                >Phone invitation</button
              >
              <button
                class="secondary"
                onclick={() => (changingPinFor = member.id)}>New PIN</button
              >
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
              <button class="secondary" onclick={() => archive(member)}
                >Has left</button
              >
            </div>
          {/if}
        </div>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
