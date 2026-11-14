<script lang="ts">
  // A person's picture, or their initials when there isn't one (or it can't be fetched, such as without a signal).
  let {
    id,
    name,
    pictureAt,
    size = 40,
  }: {
    id: string;
    name: string;
    pictureAt?: string | null;
    size?: number;
  } = $props();

  let failed = $state(false);

  const initials = $derived(
    name
      .split(/\s+/u)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0])
      .join("")
      .toUpperCase(),
  );

  // The same person always gets the same colour; a well-mixed hash so similar ids still look different.
  const hue = $derived.by(() => {
    let hash = 2166136261;

    for (const letter of id) {
      hash = Math.imul(hash ^ letter.charCodeAt(0), 16777619);
    }

    // A final stir, so ids that differ only at the end still land far apart.
    hash = Math.imul(hash ^ (hash >>> 16), 0x85ebca6b);
    hash = Math.imul(hash ^ (hash >>> 13), 0xc2b2ae35);
    return ((hash ^ (hash >>> 16)) >>> 0) % 360;
  });

  // The picture's address carries when it changed, so a new one is fetched and an old one can be kept.
  const version = $derived(pictureAt ? Date.parse(pictureAt) : 0);

  // A new picture gets another try, even if the last one couldn't be fetched.
  $effect.pre(() => {
    void version;
    failed = false;
  });
</script>

{#if pictureAt && !failed}
  <img
    class="avatar"
    src="/api/people/{id}/picture?v={version}"
    alt=""
    width={size}
    height={size}
    style:--avatar-size="{size}px"
    onerror={() => (failed = true)}
  />
{:else}
  <span
    class="avatar initials"
    aria-hidden="true"
    style:--avatar-size="{size}px"
    style:--avatar-hue={hue}>{initials}</span
  >
{/if}
