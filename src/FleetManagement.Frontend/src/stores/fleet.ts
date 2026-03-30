// src/stores/fleet.ts — reactive state management
import { writable, derived } from 'svelte/store';
import type { HubConnection } from '@microsoft/signalr';
import type { Vehicle, Route, FleetSummary, TelemetryEvent, Trip } from '$lib/api';
import { vehicles as vehiclesApi, routes as routesApi, fleet as fleetApi, trips as tripsApi, createHubConnection } from '$lib/api';

// ── Raw stores ────────────────────────────────────────────────

export const vehicleList   = writable<Vehicle[]>([]);
export const routeList     = writable<Route[]>([]);
export const tripList      = writable<Trip[]>([]);
export const fleetSummary  = writable<FleetSummary | null>(null);
export const telemetryMap  = writable<Record<string, TelemetryEvent>>({});
export const alerts        = writable<{ message: string; priority: string; timestamp: string; id: string }[]>([]);
export const hubStatus     = writable<'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('disconnected');
export const loading        = writable(false);
export const error          = writable<string | null>(null);

// ── Selected vehicle ──────────────────────────────────────────

export const selectedVehicleId = writable<string | null>(null);

export const selectedVehicle = derived(
  [vehicleList, selectedVehicleId],
  ([$vl, $sid]) => $vl.find(v => v.id === $sid) ?? null
);

// ── Derived stats ─────────────────────────────────────────────

export const vehiclesByStatus = derived(vehicleList, $vl => ({
  idle:        $vl.filter(v => v.status === 'Idle'),
  enRoute:     $vl.filter(v => v.status === 'En Route'),
  maintenance: $vl.filter(v => v.status === 'Maintenance'),
  charging:    $vl.filter(v => v.status === 'Charging'),
  outOfService:$vl.filter(v => v.status === 'Out Of Service'),
}));

export const lowFuelVehicles = derived(vehicleList, $vl =>
  $vl.filter(v => v.fuelLevelPct < 20)
);

// ── Load actions ──────────────────────────────────────────────

export async function loadAll() {
  loading.set(true);
  error.set(null);
  try {
    // when any of these calls fail, no other data is displayed.
    const [vs, rs, ts, fs] = await Promise.all([
      vehiclesApi.list(),
      routesApi.list(),
      tripsApi.list(),
      fleetApi.summary()
    ]);
    vehicleList.set(vs);
    routeList.set(rs);
    tripList.set(ts);
    fleetSummary.set(fs);
  } catch (e) {
    error.set((e as Error).message);
  } finally {
    loading.set(false);
  }
}

export async function refreshSummary() {
  try {
    const fs = await fleetApi.summary();
    fleetSummary.set(fs);
  } catch { /* silent */ }
}

// ── SignalR hub ───────────────────────────────────────────────

let hub: HubConnection | null = null;

export async function connectHub() {
  if (hub) return;
  hub = createHubConnection();

  hub.on('OnTelemetry', (ev: TelemetryEvent) => {
    telemetryMap.update(m => ({ ...m, [ev.vehicleId]: ev }));
    // Update live vehicle location
    vehicleList.update(vs => vs.map(v =>
      v.id === ev.vehicleId
        ? { ...v, currentLocation: { latitude: ev.lat, longitude: ev.lon }, speedKmh: ev.speedKmh, fuelLevelPct: ev.fuelPct }
        : v
    ));
  });

  hub.on('OnFleetSummary', (summary: FleetSummary) => {
    fleetSummary.set(summary);
  });

  hub.on('OnRouteUpdate', (routeUpdate: Partial<Route> & { routeId: string }) => {
    routeList.update(rs => rs.map(r =>
      r.id === routeUpdate.routeId ? { ...r, ...routeUpdate } : r
    ));
  });

  hub.on('OnAlert', (alert: { message: string; priority: string; timestamp: string }) => {
    alerts.update(as => [{ ...alert, id: crypto.randomUUID() }, ...as.slice(0, 49)]);
  });

  hub.onreconnecting(() => hubStatus.set('reconnecting'));
  hub.onreconnected(async () => {
    hubStatus.set('connected');
    await loadAll();
  });
  hub.onclose(() => hubStatus.set('disconnected'));

  hubStatus.set('connecting');
  await hub.start();
  hubStatus.set('connected');
}

export async function disconnectHub() {
  if (hub) {
    await hub.stop();
    hub = null;
  }
}

export async function subscribeVehicle(id: string) {
  await hub?.invoke('SubscribeVehicle', id);
}
