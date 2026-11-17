/** Light or dark backoffice, chosen per browser (default: the device's own setting). */
export type Theme = "light" | "dark";
const KEY = "mh-theme";

export function savedTheme(): Theme | null {
  try { const v = localStorage.getItem(KEY); return v === "dark" || v === "light" ? v : null; } catch { return null; }
}

export function currentTheme(): Theme {
  return savedTheme() ?? (typeof matchMedia !== "undefined" && matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
}

export function applyTheme(theme: Theme = currentTheme()) {
  const root = document.documentElement;
  root.classList.toggle("mh-dark", theme === "dark");
  root.style.colorScheme = theme;
}

export function setTheme(theme: Theme) {
  try { localStorage.setItem(KEY, theme); } catch { /* private window */ }
  applyTheme(theme);
}
