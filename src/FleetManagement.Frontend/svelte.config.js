import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

export default {
  preprocess: vitePreprocess(),
  compilerOptions: {
    // Allow Svelte 4 legacy syntax ($:, on:event) alongside Svelte 5
    runes: false
  }
};
