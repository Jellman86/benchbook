<script lang="ts">
  import jsQR from "jsqr";
  import { onMount } from "svelte";

  let {
    onScan,
    onCancel,
  }: { onScan: (code: string) => void; onCancel: () => void } = $props();

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

        if (code?.data) {
          finished = true;
          onScan(code.data);
          return;
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

<main>
  <h1>Scan a label</h1>
  <!-- playsinline stops iPhones taking the video full screen. -->
  <video bind:this={video} class="camera" playsinline muted></video>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button class="back" onclick={onCancel}>Back</button>
</main>
