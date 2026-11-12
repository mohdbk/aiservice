// UAE Design System fonts, bundled with the app (no internet needed).
import "@fontsource-variable/roboto";
import "@fontsource-variable/inter";
import "@fontsource-variable/noto-kufi-arabic";
import "@fontsource-variable/alexandria";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { AuthProvider, ToastProvider } from "./core";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
          <ErrorBoundary>
            <App />
          </ErrorBoundary>
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
