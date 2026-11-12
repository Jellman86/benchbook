import type { Me } from "./boot";
import type { Schemas } from "./types";

const keys = {
  me: "growlog.me",
  containers: "growlog.containers",
  places: "growlog.places",
};

function read<T>(key: string): T | null {
  const value = localStorage.getItem(key);
  return value === null ? null : (JSON.parse(value) as T);
}

export function rememberMe(me: Me | null) {
  if (me) {
    localStorage.setItem(keys.me, JSON.stringify(me));
  } else {
    localStorage.removeItem(keys.me);
  }
}

/** The last person signed in, for working with no signal; never on a bench, where anyone could pick it up. */
export const rememberedMe = () => (isBenchDevice() ? null : read<Me>(keys.me));

const benchKey = "growlog.benchDevice";

export function isBenchDevice(): boolean {
  try {
    return localStorage.getItem(benchKey) === "yes";
  } catch {
    return false;
  }
}

export function rememberBench(atBench: boolean) {
  try {
    if (atBench) {
      localStorage.setItem(benchKey, "yes");
    } else {
      localStorage.removeItem(benchKey);
    }
  } catch {
    // Without storage nothing is remembered, which is the safe way round.
  }
}

export function rememberContainers(containers: Schemas["ContainerResponse"][]) {
  localStorage.setItem(keys.containers, JSON.stringify(containers));
}

/** Every tray this device last saw with plants in it. */
export function rememberedContainers(): Schemas["ContainerResponse"][] {
  return read<Schemas["ContainerResponse"][]>(keys.containers) ?? [];
}

export function rememberedContainer(label: string) {
  return (
    read<Schemas["ContainerResponse"][]>(keys.containers)?.find(
      (container) =>
        container.labelCode.toLowerCase() === label.trim().toLowerCase(),
    ) ?? null
  );
}

export function rememberPlaces(places: Schemas["LocationResponse"][]) {
  localStorage.setItem(keys.places, JSON.stringify(places));
}

export const rememberedPlaces = () =>
  read<Schemas["LocationResponse"][]>(keys.places) ?? [];
