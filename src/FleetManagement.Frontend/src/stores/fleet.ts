// src/stores/fleet.ts — reactive state management
import { writable, derived, get } from 'svelte/store';
import type { HubConnection } from '@microsoft/signalr';
import type { Vehicle, Route, FleetSummary, TelemetryEvent, Trip, AlertRecord } from '$lib/api';
import { vehicles as vehiclesApi, routes as routesApi, fleet as fleetApi, trips as tripsApi, alertsApi, createHubConnection } from '$lib/api';

// ── Raw stores ────────────────────────────────────────────────

export const vehicleList   = writable<Vehicle[]>([]);
export const routeList     = writable<Route[]>([]);
export const tripList      = writable<Trip[]>([]);
export const fleetSummary  = writable<FleetSummary | null>(null);
export const telemetryMap  = writable<Record<string, TelemetryEvent>>({});
export const alerts        = writable<{ message: string; priority: string; timestamp: string; id: string; vehicleId?: string }[]>([]);
export const hubStatus     = writable<'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('disconnected');
export const loading        = writable(false);
export const error          = writable<string | null>(null);

// ── Load alerts from API ────────────────────────────────────

export async function loadAlerts() {
  try {
    const dbAlerts = await alertsApi.list();
    const signalRAlerts = get(alerts);
    // Merge DB alerts with SignalR alerts, avoiding duplicates by id
    const existingIds = new Set(signalRAlerts.map(a => a.id));
    const newAlerts = dbAlerts
      .filter(a => !existingIds.has(a.id))
      .map(a => ({
        id: a.id,
        message: a.message,
        priority: 'Normal', // Historical alerts don't have priority in DB
        timestamp: a.issuedAt,
        vehicleId: a.vehicleId ?? undefined
      }));
    if (newAlerts.length > 0) {
      alerts.update(as => [...newAlerts, ...as]);
    }
  } catch (e) {
    console.error('Failed to load alerts from API:', e);
  }
}

// ── Selected vehicle ──────────────────────────────────────────

export const selectedVehicleId = writable<string | null>(null);

// ── Map focus vehicle (for navigating from alerts) ────────────

export const mapFocusVehicleId = writable<string | null>(null);

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
    // Load summary first so the dashboard appears quickly
    const fs = await fleetApi.summary();
    fleetSummary.set(fs);

    // Then load the rest in parallel
    const [vs, rs, ts] = await Promise.all([
      vehiclesApi.list(),
      routesApi.list(),
      tripsApi.list()
    ]);
    vehicleList.set(vs);
    routeList.set(rs);
    tripList.set(ts);
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

  hub.on('OnAlert', (alert: { message: string; priority: string; timestamp: string; vehicleId?: string }) => {
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
