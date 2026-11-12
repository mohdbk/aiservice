import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// In development the .NET API runs on :5080 (dotnet run in backend/src/Moet.Recruitment.Api).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5174, proxy: { "/api": "http://localhost:5080" } },
  build: { outDir: "dist", sourcemap: false, assetsInlineLimit: 0 },
});
