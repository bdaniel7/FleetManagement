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
    // Fallback all non-asset requests to index.html for history-mode routing
    historyApiFallback: true,
    proxy: {
      '/api':  { target: 'http://localhost:5000', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5000', changeOrigin: true, ws: true }
    }
  },
  preview: {
    // Same fallback for vite preview
    historyApiFallback: true
  }
});
