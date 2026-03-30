<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { vehicleList, selectedVehicleId, telemetryMap, tripList, subscribeVehicle } from '../stores/fleet';
  import type { Vehicle, Trip } from '$lib/api';

  let mapEl: HTMLDivElement;
  let L: typeof import('leaflet') | null = null;
  let map: any = null;
  //let markers: Record<string, any> = {};
  let markers:      Record<string, any> = {};   // vehicle markers
  let tripLayers:   Record<string, any[]> = {}; // trip id → [polyline, wp markers]

  // ── Toggle layers ──────────────────────────────────────────
  let showTrips    = true;
  let showVehicles = true;

  const statusColor: Record<string, string> = {
    Idle: '#2563eb', 'En Route': '#0ea882',
    Maintenance: '#d97706', Charging: '#7c3aed', 'Out Of Service': '#dc2626'
  };

  const tripColor: Record<string, string> = {
    Draft: '#94a3b8', Scheduled: '#3b82f6',
    InProgress: '#0ea882', Completed: '#16a34a', Cancelled: '#f43f5e'
  };

  function vehicleSvgIcon(status: string, speed: number) {
    const color = statusColor[status] ?? '#888';
    const isMoving = speed > 2;
    return `<svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg">
      <circle cx="16" cy="16" r="14" fill="${color}22" stroke="${color}" stroke-width="2.5"/>
      <circle cx="16" cy="16" r="${isMoving ? 6 : 4}" fill="${color}"/>
      ${isMoving ? `<circle cx="16" cy="16" r="10" fill="none" stroke="${color}" stroke-width="1.5" opacity="0.35"/>` : ''}
    </svg>`;
  }

  // ── Waypoint pin icon (numbered) ───────────────────────────
  function waypointIcon(L: any, index: number, total: number, color: string) {
    const isStart = index === 0;
    const isEnd   = index === total - 1;
    const bg      = isStart ? '#0ea882' : isEnd ? '#dc2626' : '#2563eb';
    const label   = isStart ? 'S' : isEnd ? 'E' : String(index);
    const html = `<div style="
      width:24px;height:24px;border-radius:50%;
      background:${bg};color:#fff;
      font-size:10px;font-weight:800;font-family:Inter,sans-serif;
      display:flex;align-items:center;justify-content:center;
      box-shadow:0 2px 6px rgba(0,0,0,0.25);
      border:2px solid #fff;">
      ${label}
    </div>`;
    return L.divIcon({ html, className: '', iconSize: [24, 24], iconAnchor: [12, 12] });
  }

  async function initMap() {
    L = await import('leaflet');
    map = L.map(mapEl, { center: [51.1657, 10.4515], zoom: 6, preferCanvas: true, zoomControl: false });
    // Light CartoDB tile layer
    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap contributors © CARTO',
      subdomains: 'abcd', maxZoom: 19
    }).addTo(map);
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    renderVehicles($vehicleList);
    renderTrips($tripList);
  }

  function renderVehicles(vehicles: Vehicle[]) {
    if (!L || !map) return;
    if (!showVehicles) { Object.values(markers).forEach(m => m.remove()); markers = {}; return; }
    const current = new Set(vehicles.map(v => v.id));
    Object.keys(markers).forEach(id => {
      if (!current.has(id)) { markers[id].remove(); delete markers[id]; }
    });
    vehicles.forEach(v => {
      const lat = v.currentLocation.latitude;
      const lon = v.currentLocation.longitude;
      const svg  = vehicleSvgIcon(v.status, v.speedKmh);
      const icon = L!.divIcon({ html: svg, className: '', iconSize: [32, 32], iconAnchor: [16, 16] });
      if (markers[v.id]) {
        markers[v.id].setLatLng([lat, lon]).setIcon(icon);
        markers[v.id]._popup?.setContent(popupContent(v));
      } else {
        const marker = L!.marker([lat, lon], { icon })
          .addTo(map)
          .bindPopup(popupContent(v), { className: 'fleet-popup' })
          .on('click', () => { selectedVehicleId.set(v.id); subscribeVehicle(v.id); });
        markers[v.id] = marker;
      }
    });
  }

  function popupContent(v: Vehicle) {
    const color = statusColor[v.status] ?? '#888';
    const t = $telemetryMap[v.id];
    return `<div style="font-family:Inter,sans-serif;min-width:180px;padding:2px 0">
      <div style="font-weight:700;color:#1e2d3d;font-size:13px;margin-bottom:6px">${v.licensePlate}</div>
      <div style="font-size:12px;color:${color};margin-bottom:4px;font-weight:600">● ${v.status}</div>
      <div style="font-size:12px;color:#4a6076">Type: ${v.vehicleType}</div>
      <div style="font-size:12px;color:#4a6076">Fuel: <b style="color:#d97706">${v.fuelLevelPct.toFixed(1)}%</b></div>
      ${t ? `<div style="font-size:12px;color:#4a6076">Speed: <b style="color:#0ea882">${t.speedKmh.toFixed(0)} km/h</b></div>` : ''}
    </div>`;
  }

  // ── Trips ──────────────────────────────────────────────────
  function renderTrips(trips: Trip[]) {
    if (!L || !map) return;

    // Remove layers for trips no longer in list
    const currentIds = new Set(trips.map(t => t.id));
    Object.keys(tripLayers).forEach(id => {
      if (!currentIds.has(id)) {
        tripLayers[id].forEach((l: any) => l.remove());
        delete tripLayers[id];
      }
    });

    if (!showTrips) {
      Object.values(tripLayers).forEach(layers => layers.forEach((l: any) => l.remove()));
      tripLayers = {};
      return;
    }

    trips.forEach(trip => {
      if (!trip.waypoints || trip.waypoints.length < 2) return;

      const color  = tripColor[trip.status] ?? '#888';
      const isDash = trip.status === 'Draft' || trip.status === 'Scheduled';
      const coords: [number, number][] = trip.waypoints.map(w => [w.coordinate.latitude, w.coordinate.longitude]);

      // Close the loop for circular trips (if last !== first already)
      const lineCoords = trip.isCircular
              ? [...coords, coords[0]]
              : coords;

      // Rebuild if trip has changed
      if (tripLayers[trip.id]) {
        tripLayers[trip.id].forEach((l: any) => l.remove());
        delete tripLayers[trip.id];
      }

      const layers: any[] = [];

      // Draw polyline
      const line = L!.polyline(lineCoords, {
        color,
        weight:    trip.status === 'InProgress' ? 4 : 3,
        opacity:   trip.status === 'Cancelled' ? 0.4 : 0.85,
        dashArray: isDash ? '8 6' : undefined,
        lineCap:   'round',
        lineJoin:  'round'
      }).addTo(map);
      line.bindPopup(tripPopup(trip), { className: 'fleet-popup' });
      layers.push(line);

      // Draw waypoint markers
      trip.waypoints.forEach((wp, i) => {
        const icon   = waypointIcon(L!, i, trip.waypoints.length, color);
        const marker = L!.marker([wp.coordinate.latitude, wp.coordinate.longitude], { icon }).addTo(map);
        marker.bindPopup(wpPopup(wp, trip), { className: 'fleet-popup' });
        layers.push(marker);
      });

      // Direction arrows on active/in-progress trips
      if (trip.status === 'InProgress' || trip.status === 'Scheduled') {
        for (let i = 0; i < lineCoords.length - 1; i++) {
          const [lat1, lon1] = lineCoords[i];
          const [lat2, lon2] = lineCoords[i + 1];
          const midLat = (lat1 + lat2) / 2;
          const midLon = (lon1 + lon2) / 2;
          const angle  = Math.atan2(lat2 - lat1, lon2 - lon1) * 180 / Math.PI;
          const arrow  = L!.divIcon({
            html: `<div style="transform:rotate(${angle - 90}deg);font-size:14px;color:${color};opacity:0.7;line-height:1">▲</div>`,
            className: '', iconSize: [16, 16], iconAnchor: [8, 8]
          });
          layers.push(L!.marker([midLat, midLon], { icon: arrow, interactive: false }).addTo(map));
        }
      }

      tripLayers[trip.id] = layers;
    });
  }

  function tripPopup(t: Trip) {
    const c = tripColor[t.status] ?? '#888';
    return `<div style="font-family:Inter,sans-serif;min-width:200px;padding:2px 0">
      <div style="font-weight:700;color:#1e2d3d;font-size:13px;margin-bottom:4px">${t.name}</div>
      <div style="font-size:12px;color:${c};font-weight:600;margin-bottom:6px">● ${t.status}</div>
      <div style="font-size:12px;color:#4a6076">📍 ${t.waypoints.length} stops · ⟷ ${t.totalDistanceKm.toFixed(1)} km</div>
      ${t.isCircular ? '<div style="font-size:11px;color:#7a92a8;margin-top:3px">↩ Circular trip</div>' : ''}
    </div>`;
  }

  function wpPopup(wp: any, t: Trip) {
    return `<div style="font-family:Inter,sans-serif;padding:2px 0">
      <div style="font-weight:700;color:#1e2d3d;font-size:13px;margin-bottom:4px">${wp.label}</div>
      <div style="font-size:11px;color:#4a6076;font-family:'DM Mono',monospace">${wp.coordinate.latitude.toFixed(5)}, ${wp.coordinate.longitude.toFixed(5)}</div>
      ${wp.dwellMin > 0 ? `<div style="font-size:11px;color:#d97706;margin-top:3px">⏱ ${wp.dwellMin} min dwell</div>` : ''}
      ${wp.notes ? `<div style="font-size:11px;color:#4a6076;margin-top:3px">${wp.notes}</div>` : ''}
      <div style="font-size:11px;color:#7a92a8;margin-top:4px">${t.name}</div>
    </div>`;
  }

  $: if (map) renderVehicles($vehicleList);
  $: if (map && Object.keys($telemetryMap).length > 0) renderVehicles($vehicleList);
  $: if (map) renderTrips($tripList);

  onMount(initMap);
  onDestroy(() => { map?.remove(); map = null; markers = {}; tripLayers = {}; });
</script>

<div class="map-page">
  <div class="map-header">
    <h1>Live Fleet Map</h1>
    <div class="controls">
      <label class="ctrl-toggle">
        <input type="checkbox" bind:checked={showVehicles} />
        <span class="ctrl-dot" style="background:#0ea882"></span>
        Vehicles
      </label>
      <label class="ctrl-toggle">
        <input type="checkbox" bind:checked={showTrips} />
        <span class="ctrl-dot" style="background:#3b82f6"></span>
        Trips
      </label>
    </div>
    <div class="map-legend">
      {#each Object.entries(statusColor) as [status, color]}
        <span class="legend-item">
          <span class="legend-dot" style="background:{color}"></span>
          {status}
        </span>
      {/each}
    </div>
  </div>

  <!-- Trip status legend -->
  <div class="trip-legend">
    <span class="tlabel">Trips:</span>
    {#each Object.entries(tripColor) as [status, color]}
      <span class="titem">
        <svg width="24" height="8"><line x1="0" y1="4" x2="24" y2="4"
                                         stroke={color} stroke-width="3"
                                         stroke-dasharray={status === 'Draft' || status === 'Scheduled' ? '6 4' : 'none'}
                                         stroke-linecap="round"/></svg>
        {status}
      </span>
    {/each}
    <span class="titem"><span style="font-size:12px;color:#888">▲</span> Direction</span>
  </div>

  <div class="map-container" bind:this={mapEl}></div>
</div>

<style>
  :global(.fleet-popup .leaflet-popup-content-wrapper) {
    background: #fff;
    border: 1px solid #d0dae6;
    border-radius: 10px;
    box-shadow: 0 4px 16px rgba(30,45,61,0.12);
  }
  :global(.fleet-popup .leaflet-popup-tip) { background: #fff; }

  .map-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; }
  .map-header {
    display: flex; align-items: center; gap: 12px;
    padding: 10px 20px;
    background: var(--bg-panel);
    border-bottom: 1px solid var(--border);
    flex-shrink: 0;
    box-shadow: var(--shadow-sm);
  }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); flex: 1; }

  .controls { display: flex; gap: 12px; margin-right: auto; }
  .ctrl-toggle {
    display: flex; align-items: center; gap: 5px;
    font-size: 12px; color: var(--text-muted); cursor: pointer; user-select: none;
  }
  .ctrl-toggle input { display: none; }
  .ctrl-dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; }
  .ctrl-toggle input:not(:checked) ~ .ctrl-dot { opacity: 0.3; }
  .ctrl-toggle input:not(:checked) ~ * { opacity: 0.4; }

  .map-legend { display: flex; gap: 12px; flex-wrap: wrap; }
  .legend-item { display: flex; align-items: center; gap: 5px; font-size: 11px; color: var(--text-muted); }
  .legend-dot  { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }

  .trip-legend {
    display: flex; align-items: center; gap: 14px; padding: 6px 20px;
    background: var(--bg-subtle); border-bottom: 1px solid var(--border);
    flex-shrink: 0; flex-wrap: wrap;
  }
  .tlabel { font-size: 10px; font-weight: 700; color: var(--text-faint); text-transform: uppercase; letter-spacing: 0.07em; }
  .titem  { display: flex; align-items: center; gap: 5px; font-size: 11px; color: var(--text-muted); }


  .map-container { flex: 1; min-height: 0; }
</style>
