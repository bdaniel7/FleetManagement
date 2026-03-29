import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';
import path from 'path';

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    alias: {
      '$lib': path.resolve('./src/lib')
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api':  { target: 'http://localhost:5000', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5000', changeOrigin: true, ws: true }
    }
  }
});
