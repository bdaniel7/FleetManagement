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

export interface Waypoint {
  nodeId      : string;
  coordinate  : GeoCoordinate
  address     : string;
  arrivalTime : string | null;
  departureTime: string | null;
  stopDurationMin: number;
}

export interface Route {
  id: string;
  vehicleId: string;
  driverId: string | null;
  totalDistanceKm: number;
  estimatedDurationMin: number;
  waypoints: Waypoint[];
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

// ── Trip types ────────────────────────────────────────────────

export interface TripWaypoint {
  order:    number;
  label:    string;
  coordinate: GeoCoordinate;
  // lat:      number;
  // lon:      number;
  notes:    string;
  dwellMin: number;
}

export interface Trip {
  id:              string;
  name:            string;
  description:     string;
  vehicleId:       string | null;
  driverId:        string | null;
  status:          'Draft' | 'Scheduled' | 'InProgress' | 'Completed' | 'Cancelled';
  isCircular:      boolean;
  totalDistanceKm: number;
  waypoints:       TripWaypoint[];
  createdAt:       string;
  updatedAt:       string;
  startedAt:       string | null;
  completedAt:     string | null;
}

export interface CreateTripRequest {
  name:        string;
  description: string;
  vehicleId?:  string;
  driverId?:   string;
  isCircular:  boolean;
  waypoints:   TripWaypoint[];
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

async function patch<T = void>(path: string, body: unknown): Promise<T> {
  const r = await fetch(`${BASE}${path}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!r.ok) throw new Error(`PATCH ${path} → ${r.status}`);
  const text = await r.text();
  return text ? JSON.parse(text) as T : undefined as T;
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
  cancel:   (id: string, reason: string) => post<void>(`/routes/${id}/cancel`, { reason }),
  updateWaypoints: (id: string, waypoints: { latitude: number; longitude: number }[], algorithm: string) =>
      patch(`/routes/${id}/waypoints`, { waypoints, algorithm }) as Promise<Route>
};

// ── Fleet API ────────────────────────────────────────────────

export const fleet = {
  summary: () => get<FleetSummary>('/fleet/summary'),
  health:  () => get<{ status: string; timestamp: string }>('/fleet/health'),
  alert:   (message: string, priority: string, vehicleId?: string) =>
             post<void>('/fleet/alert', { message, priority, vehicleId })
};

// ── Trip API ──────────────────────────────────────────────────

export const trips = {
  list:      ()              => get<Trip[]>('/trips'),
  get:       (id: string)    => get<Trip>(`/trips/${id}`),
  byStatus:  (s: string)     => get<Trip[]>(`/trips/status/${s}`),
  byVehicle: (vid: string)   => get<Trip[]>(`/trips/vehicle/${vid}`),
  create:    (req: CreateTripRequest) => post<Trip>('/trips', req),
  update:    (id: string, req: Omit<CreateTripRequest, never>) => {
    return fetch(`/api/trips/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    }).then(r => r.json() as Promise<Trip>);
  },
  start:    (id: string) => post<void>(`/trips/${id}/start`),
  complete: (id: string) => post<void>(`/trips/${id}/complete`),
  cancel:   (id: string) => post<void>(`/trips/${id}/cancel`),
  delete:   (id: string) => del(`/trips/${id}`)
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
