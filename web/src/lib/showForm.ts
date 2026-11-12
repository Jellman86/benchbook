import { tick } from "svelte";

/**
 * After "Change" on an entry lower down the list: brings the form at the top into view and puts the cursor in
 * its first box. On a phone the form is off screen, so otherwise the tap seems to do nothing.
 */
export async function showForm() {
  await tick();
  const form = document.querySelector<HTMLFormElement>("form[data-edit-form]");
  form?.scrollIntoView({ behavior: "smooth", block: "start" });
  form
    ?.querySelector<HTMLInputElement | HTMLSelectElement>("input, select")
    ?.focus({ preventScroll: true });
}
