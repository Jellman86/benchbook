// A person's sign-in tune, chosen for them by the owner. Phones only play sound that a tap started, so the tap that
// signs someone in first "unlocks" one audio player with a moment of silence, and the tune plays on that same player
// once signing in has worked. Anything going wrong here is silent: it's only a bit of fun.

const pendingKey = "hortiplanner.tunePending";

// A tiny silent WAV, enough to count as playing.
const silence =
  "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=";

let player: HTMLAudioElement | null = null;

/** Call straight from the tap that signs someone in, before anything is awaited. */
export function unlockTune() {
  try {
    player ??= new Audio();
    player.src = silence;
    void player.play().catch(() => {});
  } catch {
    // No audio here; there's simply no tune.
  }
}

/** Plays the signed-in person's tune, if they have one switched on. */
export function playTune() {
  try {
    player ??= new Audio();
    // A fresh address each time, so a tune the owner has just changed is the one heard.
    player.src = `/api/account/tune?at=${Date.now()}`;
    void player.play().catch(() => {});
  } catch {
    // As above.
  }
}

/** Stops a tune that's playing, for example when someone turns theirs off. */
export function stopTune() {
  player?.pause();
}

/**
 * Signing in through the shared staff login leaves the page, so there's no tap left to play from. Note it before
 * leaving, and play on the first tap after coming back.
 */
export function rememberTuneForReturn() {
  try {
    sessionStorage.setItem(pendingKey, "1");
  } catch {
    // Without storage the tune is skipped.
  }
}

export function playTuneOnFirstTapIfReturning() {
  try {
    if (!sessionStorage.getItem(pendingKey)) {
      return;
    }

    sessionStorage.removeItem(pendingKey);
  } catch {
    return;
  }

  window.addEventListener("pointerdown", () => playTune(), {
    once: true,
    capture: true,
  });
}
