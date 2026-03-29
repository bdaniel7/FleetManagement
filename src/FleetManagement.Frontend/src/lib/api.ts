// src/lib/api.ts — typed API client + SignalR hub

import * as signalR from '@microsoft/signalr';

const BASE = '/api';

// ── Types ────────────────────────────────────────────────────

export interface GeoCoordinate { latitude: number; longitude: number; }

export interface Vehicle {
  id: string;
  licensePlate: string;
  vehicleType: string;
  status: string;
  currentLocation: GeoCoordinate;
  assignedDriver: string | null;
  fuelLevelPct: number;
  speedKmh: number;
  maxPayloadKg: number;
  currentPayloadKg: number;
  createdAt: string;
  updatedAt: string;
}

export interface Route {
  id: string;
  vehicleId: string;
  driverId: string | null;
  totalDistanceKm: number;
  estimatedDurationMin: number;
  status: string;
  priority: string;
  algorithm: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface FleetSummary {
  totalVehicles: number;
  activeVehicles: number;
  idleVehicles: number;
  maintenanceCount: number;
  totalRoutes: number;
  activeRoutes: number;
  avgFuelLevel: number;
  lastUpdated: string;
}

export interface TelemetryEvent {
  vehicleId: string;
  timestamp: string;
  lat: number;
  lon: number;
  speedKmh: number;
  fuelPct: number;
  engineTemp: number;
  odometerKm: number;
  diagCodes: string[];
}

export interface PlanRouteRequest {
  vehicleId: string;
  driverId?: string;
  waypoints: { latitude: number; longitude: number }[];
  algorithm: 'AStar' | 'Dijkstra' | 'BellmanFord';
  priority: 'Low' | 'Normal' | 'High' | 'Emergency';
}

// ── Fetch helpers ────────────────────────────────────────────

async function get<T>(path: string): Promise<T> {
  const r = await fetch(`${BASE}${path}`);
  if (!r.ok) throw new Error(`GET ${path} → ${r.status}`);
  return r.json();
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  const r = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: r.statusText }));
    throw new Error(err.error ?? r.statusText);
  }
  return r.json().catch(() => ({} as T));
}

async function patch(path: string, body: unknown): Promise<void> {
  const r = await fetch(`${BASE}${path}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!r.ok) throw new Error(`PATCH ${path} → ${r.status}`);
}

async function del(path: string): Promise<void> {
  const r = await fetch(`${BASE}${path}`, { method: 'DELETE' });
  if (!r.ok) throw new Error(`DELETE ${path} → ${r.status}`);
}

// ── Vehicle API ──────────────────────────────────────────────

export const vehicles = {
  list:          ()                              => get<Vehicle[]>('/vehicles'),
  get:           (id: string)                    => get<Vehicle>(`/vehicles/${id}`),
  byStatus:      (s: string)                     => get<Vehicle[]>(`/vehicles/status/${s}`),
  register:      (v: Omit<Vehicle,'id'|'createdAt'|'updatedAt'|'currentLocation'|'assignedDriver'> & { latitude: number; longitude: number }) =>
                   post<Vehicle>('/vehicles', v),
  updateLocation:(id: string, lat: number, lon: number, speedKmh: number) =>
                   patch(`/vehicles/${id}/location`, { latitude: lat, longitude: lon, speedKmh }),
  updateStatus:  (id: string, status: string)    => patch(`/vehicles/${id}/status`, { status }),
  delete:        (id: string)                    => del(`/vehicles/${id}`),
  sendTelemetry: (id: string, data: Partial<TelemetryEvent>) =>
                   post<void>(`/vehicles/${id}/telemetry`, data)
};

// ── Route API ────────────────────────────────────────────────

export const routes = {
  list:     ()              => get<Route[]>('/routes'),
  active:   ()              => get<Route[]>('/routes/active'),
  get:      (id: string)    => get<Route>(`/routes/${id}`),
  forVehicle:(vid: string)  => get<Route[]>(`/routes/vehicle/${vid}`),
  plan:     (req: PlanRouteRequest) => post<Route>('/routes/plan', req),
  activate: (id: string)    => post<void>(`/routes/${id}/activate`),
  complete: (id: string)    => post<void>(`/routes/${id}/complete`),
  cancel:   (id: string, reason: string) => post<void>(`/routes/${id}/cancel`, { reason })
};

// ── Fleet API ────────────────────────────────────────────────

export const fleet = {
  summary: () => get<FleetSummary>('/fleet/summary'),
  health:  () => get<{ status: string; timestamp: string }>('/fleet/health'),
  alert:   (message: string, priority: string, vehicleId?: string) =>
             post<void>('/fleet/alert', { message, priority, vehicleId })
};

// ── SignalR Hub ───────────────────────────────────────────────

export type HubEventMap = {
  OnTelemetry:    (ev: TelemetryEvent) => void;
  OnFleetSummary: (summary: FleetSummary) => void;
  OnRouteUpdate:  (route: Partial<Route>) => void;
  OnAlert:        (alert: { message: string; priority: string; timestamp: string }) => void;
};

export function createHubConnection() {
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/telemetry')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}
