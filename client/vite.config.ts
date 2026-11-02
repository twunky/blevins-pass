import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Fixed loopback ports: the API runs on 5050 and Stripe redirects back to 5173.
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5050', changeOrigin: false },
    },
  },
});
