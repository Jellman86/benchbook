/** A short sound and buzz for each scan, so someone looking at trays rather than the screen knows. */
export function signal(accepted: boolean) {
  try {
    navigator.vibrate?.(accepted ? 40 : [90, 60, 90]);
  } catch {
    // Not every device can buzz.
  }

  try {
    const sound = new AudioContext();
    const tone = sound.createOscillator();
    const volume = sound.createGain();
    tone.frequency.value = accepted ? 880 : 220;
    volume.gain.value = 0.08;
    tone.connect(volume).connect(sound.destination);
    tone.start();
    tone.stop(sound.currentTime + (accepted ? 0.08 : 0.25));
    tone.onended = () => void sound.close();
  } catch {
    // Without sound the message on screen still says what happened.
  }
}
