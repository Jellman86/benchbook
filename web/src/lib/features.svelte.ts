import { api } from "../api/client";
import type { Schemas } from "./types";

// Parts of GrowLog the owner can turn off under Settings, Features. Off means hidden everywhere; records are kept.
export type Feature = Schemas["NurseryFeature"];

const storageKey = "growlog.featuresOff";

function remembered(): Feature[] {
  try {
    const kept: unknown = JSON.parse(localStorage.getItem(storageKey) ?? "[]");
    return Array.isArray(kept) ? (kept as Feature[]) : [];
  } catch {
    return [];
  }
}

const state = $state({ off: remembered() });

/** Whether a part of GrowLog is on. Everything is on until the owner turns it off. */
export const isOn = (feature: Feature) => !state.off.includes(feature);

/** Whether any of these parts is on, for screens several parts share. */
export const anyOn = (...features: Feature[]) => features.some(isOn);

function keep(off: Feature[]) {
  state.off = off;

  try {
    localStorage.setItem(storageKey, JSON.stringify(off));
  } catch {
    // Without storage the switches still apply until the app is closed.
  }
}

/** Fetches the switches; offline, the last ones seen on this device stay in force. */
export async function loadFeatures() {
  try {
    const { data } = await api.GET("/api/settings/features");

    if (data) {
      keep(data.filter((item) => !item.on).map((item) => item.feature));
    }
  } catch {
    // Offline: keep what was remembered.
  }
}

/** Reloads the switches whenever the app comes back into view, so a change the owner made reaches every device. */
export function watchFeatures() {
  const visible = () => {
    if (document.visibilityState === "visible") {
      void loadFeatures();
    }
  };

  document.addEventListener("visibilitychange", visible);
  return () => document.removeEventListener("visibilitychange", visible);
}

/** Turns a part on or off for everyone. Only the owner can. */
export async function setFeature(feature: Feature, on: boolean) {
  const { response } = await api.PUT("/api/settings/features/{feature}", {
    params: { path: { feature } },
    body: { on },
  });

  // Shown straight away, then what the server now holds, including any switch someone else turned meanwhile.
  if (response.ok) {
    keep(
      on
        ? state.off.filter((item) => item !== feature)
        : [...state.off.filter((item) => item !== feature), feature],
    );
  }

  await loadFeatures();
  return response.ok;
}

export type FeatureInfo = { feature: Feature; label: string; hides: string };

/** What each switch is called and what turning it off hides, in the order shown under Settings, Features. */
export const featureInfo: FeatureInfo[] = [
  {
    feature: "Climate",
    label: "Sensors and climate",
    hides:
      "Manage, Climate and Settings, Sensors. Sensors' readings are refused while it's off.",
  },
  {
    feature: "PlacesMap",
    label: "Places map",
    hides: "The map in Places and Settings, Map, with its satellite pictures.",
  },
  {
    feature: "Passports",
    label: "Plant passports",
    hides:
      "Passports on trays, issuing them, received passports, consignments and the authorisation renewal reminder.",
  },
  {
    feature: "MotherPlants",
    label: "Mother plants",
    hides:
      "Manage, Mother plants and choosing a mother for cuttings in New batch. Mothers already recorded still show on their batches.",
  },
  {
    feature: "Jobs",
    label: "Jobs",
    hides: "Manage, Jobs, the jobs due on Record and the late jobs reminder.",
  },
  {
    feature: "Invoices",
    label: "Trade invoices",
    hides:
      "Invoices, the overdue invoices reminder and invoiced orders in Consignments.",
  },
  {
    feature: "Purchases",
    label: "Purchases",
    hides: "Manage, Purchases and what's owed to suppliers.",
  },
  {
    feature: "BatchPhotos",
    label: "Batch photos",
    hides: "The strip of photos on trays, Edit batch and the trace report.",
  },
];
