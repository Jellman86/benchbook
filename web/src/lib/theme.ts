export type Theme = "system" | "light" | "dark";

const themeKey = "growlog.theme";
const sidebarKey = "growlog.sidebar";

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

export function savedTheme(): Theme {
  const value = read(themeKey);
  return value === "light" || value === "dark" ? value : "system";
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
  return theme === "system" ? "light" : theme === "light" ? "dark" : "system";
}

export const savedCollapsed = () => read(sidebarKey) === "collapsed";

export function saveCollapsed(collapsed: boolean) {
  write(sidebarKey, collapsed ? "collapsed" : "open");
}
