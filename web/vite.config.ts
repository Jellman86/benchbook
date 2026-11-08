import { svelte } from "@sveltejs/vite-plugin-svelte";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [svelte()],
  server: { proxy: { "/api": "http://localhost:5000" } },
  test: { include: ["src/**/*.test.ts"] },
});
