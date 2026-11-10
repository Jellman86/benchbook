<script lang="ts">
  import jsQR from "jsqr";
  import { onMount } from "svelte";

  // Either reads one label and stops, or keeps reading one after another inside another screen.
  let {
    onScan,
    onCancel,
    continuous = false,
  }: {
    onScan: (code: string) => void;
    onCancel: () => void;
    continuous?: boolean;
  } = $props();

  const samePause = 2500;
  let lastCode = "";
  let lastAt = 0;

  let video = $state<HTMLVideoElement | null>(null);
  let problem = $state("");

  onMount(() => {
    let stream: MediaStream | null = null;
    let frame = 0;
    let finished = false;
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });

    function look() {
      if (finished || !video || !context) {
        return;
      }

      if (video.readyState === video.HAVE_ENOUGH_DATA) {
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        context.drawImage(video, 0, 0, canvas.width, canvas.height);
        const image = context.getImageData(0, 0, canvas.width, canvas.height);
        const code = jsQR(image.data, image.width, image.height);

        if (code?.data && !continuous) {
          finished = true;
          onScan(code.data);
          return;
        }

        // The same label held in view is read once, not over and over.
        if (
          code?.data &&
          (code.data !== lastCode || Date.now() - lastAt > samePause)
        ) {
          lastCode = code.data;
          lastAt = Date.now();
          onScan(code.data);
        }
      }

      frame = requestAnimationFrame(look);
    }

    navigator.mediaDevices
      .getUserMedia({ video: { facingMode: "environment" } })
      .then(async (opened) => {
        if (finished) {
          opened.getTracks().forEach((track) => track.stop());
          return;
        }

        stream = opened;

        if (video) {
          video.srcObject = opened;
          await video.play();
          look();
        }
      })
      .catch(() => {
        problem =
          "The camera isn't available. Check GrowLog is allowed to use it.";
      });

    return () => {
      finished = true;
      cancelAnimationFrame(frame);
      stream?.getTracks().forEach((track) => track.stop());
    };
  });
</script>

{#if continuous}
  <div class="stack">
    <!-- playsinline stops iPhones taking the video full screen. -->
    <video bind:this={video} class="camera" playsinline muted></video>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <button class="secondary" onclick={onCancel}>Stop the camera</button>
  </div>
{:else}
  <main>
    <h1>Scan a label</h1>
    <video bind:this={video} class="camera" playsinline muted></video>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <button class="back" onclick={onCancel}>Back</button>
  </main>
{/if}
