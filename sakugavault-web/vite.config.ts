import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const proxyTarget = process.env.VITE_PROXY_TARGET ?? "http://localhost:8080";

export default defineConfig({
  plugins: [react()],
  build: {
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks: {
          "react-vendor": ["react", "react-dom", "react-router-dom"],
          player: ["hls.js"]
        }
      }
    }
  },
  server: {
    host: "0.0.0.0",
    allowedHosts: ["host.docker.internal"],
    port: 5173,
    proxy: {
      "/api": proxyTarget,
      "/health": proxyTarget
    }
  },
  preview: {
    host: "0.0.0.0",
    port: 4173
  }
});
