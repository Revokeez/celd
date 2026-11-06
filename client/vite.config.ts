import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the API runs on http://localhost:5080. Proxying keeps the browser on one origin,
// so the SameSite=Strict session and CSRF cookies work without any CORS configuration.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: false },
    },
  },
});
