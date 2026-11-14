export type Theme = "system" | "light" | "dark";

const themeKey = "hortiplanner.theme";
const sidebarKey = "hortiplanner.sidebar";

export const themes: { theme: Theme; label: string }[] = [
  { theme: "system", label: "Match device" },
  { theme: "light", label: "Light" },
  { theme: "dark", label: "Dark" },
];

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // A private window can refuse storage. The choice just won't be remembered.
  }
}

/** Light unless someone has chosen otherwise on this device; index.html applies the same rule before drawing. */
export function savedTheme(): Theme {
  const value = read(themeKey);
  return value === "dark" || value === "system" ? value : "light";
}

export function applyTheme(theme: Theme) {
  write(themeKey, theme);

  if (theme === "system") {
    delete document.documentElement.dataset.theme;
  } else {
    document.documentElement.dataset.theme = theme;
  }
}

export function nextTheme(theme: Theme): Theme {
  return theme === "light" ? "dark" : theme === "dark" ? "system" : "light";
}

export const savedCollapsed = () => read(sidebarKey) === "collapsed";

export function saveCollapsed(collapsed: boolean) {
  write(sidebarKey, collapsed ? "collapsed" : "open");
}
