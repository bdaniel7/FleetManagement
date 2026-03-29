<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { vehicleList, selectedVehicleId, telemetryMap, subscribeVehicle } from '../stores/fleet';
  import type { Vehicle } from '$lib/api';

  let mapEl: HTMLDivElement;
  let L: typeof import('leaflet') | null = null;
  let map: any = null;
  let markers: Record<string, any> = {};

  const statusColor: Record<string, string> = {
    Idle: '#2563eb', 'En Route': '#0ea882',
    Maintenance: '#d97706', Charging: '#7c3aed', 'Out Of Service': '#dc2626'
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
  }

  function renderVehicles(vehicles: Vehicle[]) {
    if (!L || !map) return;
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

  $: if (map) renderVehicles($vehicleList);
  $: if (map && Object.keys($telemetryMap).length > 0) renderVehicles($vehicleList);

  onMount(initMap);
  onDestroy(() => { map?.remove(); map = null; markers = {}; });
</script>

<div class="map-page">
  <div class="map-header">
    <h1>Live Fleet Map</h1>
    <div class="map-legend">
      {#each Object.entries(statusColor) as [status, color]}
        <span class="legend-item">
          <span class="legend-dot" style="background:{color}"></span>
          {status}
        </span>
      {/each}
    </div>
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
    display: flex; align-items: center; gap: 14px;
    padding: 14px 24px;
    background: var(--bg-panel);
    border-bottom: 1px solid var(--border);
    flex-shrink: 0;
    box-shadow: var(--shadow-sm);
  }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); flex: 1; }
  .map-legend { display: flex; gap: 14px; flex-wrap: wrap; }
  .legend-item { display: flex; align-items: center; gap: 5px; font-size: 12px; color: var(--text-muted); }
  .legend-dot  { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; }
  .map-container { flex: 1; min-height: 0; }
</style>
