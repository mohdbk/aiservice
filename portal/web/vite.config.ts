import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// In development the API runs on :8080 (cd ../server && npm run dev, which uses the mock Frappe).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5173, proxy: { "/api": "http://localhost:8080" } },
  build: { outDir: "dist", sourcemap: false, assetsInlineLimit: 0 },
});
