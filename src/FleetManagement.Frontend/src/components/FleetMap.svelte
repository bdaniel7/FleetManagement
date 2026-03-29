<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { vehicleList, selectedVehicleId, telemetryMap, subscribeVehicle } from '../stores/fleet';
  import type { Vehicle } from '$lib/api';

  let mapEl: HTMLDivElement;
  let L: typeof import('leaflet') | null = null;
  let map: any = null;
  let markers: Record<string, any> = {};

  const statusColor: Record<string, string> = {
    Idle: '#3b82f6', 'En Route': '#22d3a5',
    Maintenance: '#f59e0b', Charging: '#a78bfa', 'Out Of Service': '#f43f5e'
  };

  function vehicleSvgIcon(status: string, speed: number) {
    const color = statusColor[status] ?? '#888';
    const isMoving = speed > 2;
    return `
      <svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg">
        <circle cx="16" cy="16" r="14" fill="${color}22" stroke="${color}" stroke-width="2"/>
        <circle cx="16" cy="16" r="${isMoving ? 6 : 4}" fill="${color}"/>
        ${isMoving ? `<circle cx="16" cy="16" r="10" fill="none" stroke="${color}" stroke-width="1" opacity="0.4"/>` : ''}
      </svg>`;
  }

  async function initMap() {
    L = await import('leaflet');

    map = L.map(mapEl, {
      center: [44.43, 26.10],
      zoom: 12,
      preferCanvas: true,
      zoomControl: false
    });

    // Dark tile layer (OpenStreetMap + CartoDB dark)
    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap contributors © CARTO',
      subdomains: 'abcd',
      maxZoom: 19
    }).addTo(map);

    L.control.zoom({ position: 'bottomright' }).addTo(map);
    renderVehicles($vehicleList);
  }

  function renderVehicles(vehicles: Vehicle[]) {
    if (!L || !map) return;

    // Remove stale markers
    const current = new Set(vehicles.map(v => v.id));
    Object.keys(markers).forEach(id => {
      if (!current.has(id)) { markers[id].remove(); delete markers[id]; }
    });

    vehicles.forEach(v => {
      const lat = v.currentLocation.latitude;
      const lon = v.currentLocation.longitude;
      const svg = vehicleSvgIcon(v.status, v.speedKmh);
      const icon = L!.divIcon({ html: svg, className: '', iconSize: [32, 32], iconAnchor: [16, 16] });

      if (markers[v.id]) {
        markers[v.id].setLatLng([lat, lon]).setIcon(icon);
        markers[v.id]._popup?.setContent(popupContent(v));
      } else {
        const marker = L!.marker([lat, lon], { icon })
          .addTo(map)
          .bindPopup(popupContent(v), { className: 'fleet-popup' })
          .on('click', () => {
            selectedVehicleId.set(v.id);
            subscribeVehicle(v.id);
          });
        markers[v.id] = marker;
      }
    });
  }

  function popupContent(v: Vehicle) {
    const color = statusColor[v.status] ?? '#888';
    const telemetry = $telemetryMap[v.id];
    return `
      <div style="font-family:'DM Mono',monospace;min-width:180px;">
        <div style="font-weight:700;color:#e2eaf4;font-size:13px;margin-bottom:6px">${v.licensePlate}</div>
        <div style="font-size:11px;color:${color};margin-bottom:4px">● ${v.status}</div>
        <div style="font-size:11px;color:#6b84a0">Type: ${v.vehicleType}</div>
        <div style="font-size:11px;color:#6b84a0">Fuel: <b style="color:#f59e0b">${v.fuelLevelPct.toFixed(1)}%</b></div>
        ${telemetry ? `<div style="font-size:11px;color:#6b84a0">Speed: <b style="color:#22d3a5">${telemetry.speedKmh.toFixed(0)} km/h</b></div>` : ''}
      </div>`;
  }

  // Reactively update markers when vehicles or telemetry change
  $: if (map) renderVehicles($vehicleList);
  $: if (map && Object.keys($telemetryMap).length > 0) renderVehicles($vehicleList);

  onMount(initMap);
  onDestroy(() => { map?.remove(); map = null; markers = {}; });
</script>

<div class="map-page">
  <div class="map-header">
    <span class="title-icon">◎</span>
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
    background: #0d1420;
    border: 1px solid #1e2d45;
    border-radius: 8px;
    box-shadow: 0 8px 32px #00000080;
  }
  :global(.fleet-popup .leaflet-popup-tip) { background: #0d1420; }

  .map-page {
    display: flex;
    flex-direction: column;
    height: 100%;
    overflow: hidden;
  }
  .map-header {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 16px 24px;
    border-bottom: 1px solid #1e2d45;
    flex-shrink: 0;
  }
  .title-icon { font-size: 24px; color: #22d3a5; }
  h1 { font-size: 18px; font-weight: 700; color: #e2eaf4; letter-spacing: 0.04em; flex: 1; }

  .map-legend {
    display: flex;
    gap: 14px;
  }
  .legend-item {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 11px;
    color: #6b84a0;
    letter-spacing: 0.04em;
  }
  .legend-dot {
    width: 8px; height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .map-container {
    flex: 1;
    min-height: 0;
  }
</style>
