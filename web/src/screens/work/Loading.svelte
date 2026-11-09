<script lang="ts">
  import Sprout from "@lucide/svelte/icons/sprout";

  let { text }: { text: string } = $props();
</script>

<p class="loading-line" role="status"><Sprout size={20} />{text}</p>
