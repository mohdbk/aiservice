import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// In development the .NET API runs on :5080 (dotnet run in backend/src/Moet.Recruitment.Api).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5174, proxy: { "/api": "http://localhost:5080" } },
  // Used by the end-to-end check in CI (same origin as nginx in production).
  // changeOrigin stays false so the API's same-origin check sees the browser's host, as behind nginx.
  preview: { port: 4173, proxy: { "/api": { target: process.env.API_URL || "http://localhost:5080", changeOrigin: false } } },
  build: { outDir: "dist", sourcemap: false, assetsInlineLimit: 0 },
});
