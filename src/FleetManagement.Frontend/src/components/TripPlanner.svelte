<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { tripList, vehicleList, loadAll } from '../stores/fleet';
  import { trips as tripsApi } from '$lib/api';
  import type { Trip, TripWaypoint, CreateTripRequest } from '$lib/api';

  // ── Leaflet ────────────────────────────────────────────────
  let mapEl: HTMLDivElement;
  let L: typeof import('leaflet') | null = null;
  let map: any = null;

  // Map layers
  let wpMarkers:   any[] = [];   // one marker per waypoint
  let polyline:    any = null;   // connecting line
  let clickCursor: any = null;   // temporary "click here" pulse marker

  // ── Form state ─────────────────────────────────────────────
  type WpForm = {
    label: string;
    lat: string;
    lon: string;
    notes: string;
    dwellMin: string;
    pending:  boolean;  // true = waiting for map click
  };

  let name        = '';
  let description = '';
  let vehicleId   = '';
  let isCircular  = false;
  let waypoints: WpForm[] = [];
  // let waypoints: WpForm[] = [
  //   { label: 'Start', lat: '', lon: '', notes: '', dwellMin: '0' },
  //   { label: 'End',   lat: '', lon: '', notes: '', dwellMin: '0' }
  // ];

  let saving  = false;
  let formErr = '';

  // Index of the waypoint currently waiting for a map click (-1 = none)
  let activeWpIdx = -1;

  // ── Editing an existing trip ───────────────────────────────
  let editingId: string | null = null;

  function resetForm() {
    editingId   = null;
    name        = '';
    description = '';
    vehicleId   = '';
    isCircular  = false;
    waypoints   = [];
    activeWpIdx = -1;
    formErr     = '';
    clearMapLayers();
  }

  // function newForm() {
  //   editingId   = null;
  //   name        = '';
  //   description = '';
  //   vehicleId   = '';
  //   isCircular  = false;
  //   waypoints   = [
  //     { label: 'Start', lat: '', lon: '', notes: '', dwellMin: '0' },
  //     { label: 'End',   lat: '', lon: '', notes: '', dwellMin: '0' }
  //   ];
  //   formErr = '';
  // }

  function loadIntoForm(t: Trip) {
    editingId   = t.id;
    name        = t.name;
    description = t.description;
    vehicleId   = t.vehicleId ?? '';
    isCircular  = t.isCircular;
    waypoints   = t.waypoints.map(w => ({
      label:   w.label,
      lat:     String(w.coordinate.latitude),
      lon:     String(w.coordinate.longitude),
      notes:   w.notes,
      dwellMin:String(w.dwellMin),
      pending: false
    }));
    activeWpIdx = -1;
    formErr     = '';
    renderMapWaypoints();
  }

  // ── Add / remove stops ─────────────────────────────────────
  function addStop() {
    const isFirst = waypoints.length === 0;
    waypoints = [...waypoints, {
      label:    isFirst ? 'Start' : waypoints.length === 1 ? 'End' : '',
      lat:      '',
      lon:      '',
      notes:    '',
      dwellMin: '0',
      pending:  true
    }];
    activeWpIdx = waypoints.length - 1;
    setMapClickCursor(activeWpIdx);
  }

  function removeStop(i: number) {
    if (waypoints.length <= 1) { waypoints = []; return; }
    waypoints = waypoints.filter((_, idx) => idx !== i);
    if (activeWpIdx === i)     activeWpIdx = -1;
    else if (activeWpIdx > i)  activeWpIdx -= 1;
    renderMapWaypoints();
    removeClickCursor();
  }

  function activateStop(i: number) {
    activeWpIdx = i;
    setMapClickCursor(i);
  }


  // ── Waypoint management ────────────────────────────────────
  // function addWp() {
  //   waypoints = [...waypoints, { label: '', lat: '', lon: '', notes: '', dwellMin: '0' }];
  // }
  // function removeWp(i: number) {
  //   if (waypoints.length <= 2) return;
  //   waypoints = waypoints.filter((_, idx) => idx !== i);
  // }
  // function moveUp(i: number) {
  //   if (i === 0) return;
  //   const arr = [...waypoints];
  //   [arr[i-1], arr[i]] = [arr[i], arr[i-1]];
  //   waypoints = arr;
  // }
  // function moveDown(i: number) {
  //   if (i === waypoints.length - 1) return;
  //   const arr = [...waypoints];
  //   [arr[i], arr[i+1]] = [arr[i+1], arr[i]];
  //   waypoints = arr;
  // }

  // ── Circular: sync last wp to first ───────────────────────
  $: if (isCircular && waypoints.length >= 2) {
    const first = waypoints[0];
    const last  = waypoints[waypoints.length - 1];
    if (first.lat && first.lon &&
            (last.lat !== first.lat || last.lon !== first.lon)) {
      const circularEnd = {
        label: (first.label || 'Start') + ' (return)',
        lat: first.lat, lon: first.lon,
        notes: '', dwellMin: '0', pending: false
      };
      waypoints = [...waypoints.slice(0, -1), circularEnd];
      renderMapWaypoints();
    }
  }

  // ── Map init ───────────────────────────────────────────────
  async function initMap() {
    L = await import('leaflet');
    map = L.map(mapEl, {
      center: [50.920185, 10.566824], zoom: 7,
      zoomControl: false
    });
    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap contributors © CARTO',
      subdomains: 'abcd', maxZoom: 19
    }).addTo(map);
    L.control.zoom({ position: 'bottomright' }).addTo(map);

    map.on('click', onMapClick);
  }

  // ── Map click handler ──────────────────────────────────────
  function onMapClick(e: any) {
    if (activeWpIdx < 0 || !map) return;

    const { lat, lng } = e.latlng;
    waypoints = waypoints.map((wp, i) =>
            i === activeWpIdx
                    ? { ...wp, lat: lat.toFixed(6), lon: lng.toFixed(6), pending: false }
                    : wp
    );
    activeWpIdx = -1;
    removeClickCursor();
    renderMapWaypoints();
    map.getContainer().style.cursor = '';
  }

  // ── Click cursor (pulsing ring at last known position) ─────
  function setMapClickCursor(wpIdx: number) {
    if (!L || !map) return;
    removeClickCursor();
    map.getContainer().style.cursor = 'crosshair';

    // If the waypoint already has coords, center map there
    const wp = waypoints[wpIdx];
    if (wp?.lat && wp?.lon) {
      const lat = parseFloat(wp.lat);
      const lon = parseFloat(wp.lon);
      if (!isNaN(lat) && !isNaN(lon)) {
        map.panTo([lat, lon]);
      }
    }
  }

  function removeClickCursor() {
    if (clickCursor) { clickCursor.remove(); clickCursor = null; }
    if (map) map.getContainer().style.cursor = '';
  }

  // ── Render waypoints on map ────────────────────────────────
  function waypointIcon(index: number, total: number) {
    const isStart = index === 0;
    const isEnd   = index === total - 1 && total > 1;
    const bg      = isStart ? '#0ea882' : isEnd ? '#dc2626' : '#2563eb';
    const label   = isStart ? 'S' : isEnd ? 'E' : String(index);
    const html    = `<div style="
      width:28px;height:28px;border-radius:50%;
      background:${bg};color:#fff;
      font-size:11px;font-weight:800;font-family:Inter,sans-serif;
      display:flex;align-items:center;justify-content:center;
      box-shadow:0 2px 8px rgba(0,0,0,0.3);border:2.5px solid #fff;">
      ${label}
    </div>`;
    return L!.divIcon({ html, className: '', iconSize: [28, 28], iconAnchor: [14, 14] });
  }

  function clearMapLayers() {
    wpMarkers.forEach(m => m?.remove());
    wpMarkers = [];
    if (polyline)  { polyline.remove();  polyline  = null; }
  }

  function renderMapWaypoints() {
    if (!L || !map) return;
    clearMapLayers();

    const valid = waypoints
            .map((wp, i) => ({ wp, i }))
            .filter(({ wp }) => wp.lat && wp.lon && !isNaN(parseFloat(wp.lat)) && !isNaN(parseFloat(wp.lon)));

    if (valid.length === 0) return;

    const coords: [number, number][] = valid.map(({ wp }) => [parseFloat(wp.lat), parseFloat(wp.lon)]);

    // Draw polyline
    const lineCoords = isCircular && coords.length > 1 ? [...coords, coords[0]] : coords;
    polyline = L.polyline(lineCoords, { color: '#0ea882', weight: 3, dashArray: '6 4', opacity: 0.8 }).addTo(map);

    // Place numbered markers
    valid.forEach(({ wp, i }) => {
      const marker = L!.marker(
              [parseFloat(wp.lat), parseFloat(wp.lon)],
              { icon: waypointIcon(i, waypoints.length) }
      ).addTo(map).bindTooltip(wp.label || `Stop ${i + 1}`, { permanent: false });
      wpMarkers.push(marker);
    });

    // Fit map to show all waypoints
    if (coords.length >= 2) {
      map.fitBounds(L.latLngBounds(coords), { padding: [40, 40] });
    } else if (coords.length === 1) {
      map.setView(coords[0], 13);
    }
  }

  // Reactively re-render map when waypoints change
  $: if (map) renderMapWaypoints();

  // ── Save / update ──────────────────────────────────────────
  async function save() {
    formErr = '';
    if (!name.trim()) { formErr = 'Trip name is required'; return; }
    if (waypoints.length < 2) { formErr = 'At least 2 waypoints required'; return; }

    const parsed = waypoints.map((w, i) => ({
      order:    i,
      label:    w.label.trim() || `Stop ${i + 1}`,
      // lat:      parseFloat(w.lat),
      // lon:      parseFloat(w.lon),
      coordinate: {latitude: parseFloat(w.lat), longitude: parseFloat(w.lon)},
      notes:    w.notes,
      dwellMin: parseInt(w.dwellMin) || 0
    }));

    if (parsed.length < 2) { formErr = 'Add at least 2 stops'; return; }
    if (parsed.some(p => isNaN(p.coordinate.latitude) || isNaN(p.coordinate.longitude))) {
      formErr = 'Click on the map to set coordinates for all stops'; return;
    }

    const req: CreateTripRequest = {
      name:        name.trim(),
      description: description.trim(),
      vehicleId:   vehicleId || undefined,
      isCircular,
      waypoints:   parsed
    };

    saving = true;
    try {
      if (editingId) {
        await tripsApi.update(editingId, req);
      } else {
        await tripsApi.create(req);
      }
      await loadAll();
      resetForm();
    } catch (e) {
      formErr = (e as Error).message;
    } finally {
      saving = false;
    }
  }

  // ── Trip actions ───────────────────────────────────────────
  async function startTrip(id: string)    { await tripsApi.start(id);    await loadAll(); }
  async function completeTrip(id: string) { await tripsApi.complete(id); await loadAll(); }
  async function cancelTrip(id: string)   { await tripsApi.cancel(id);   await loadAll(); }
  async function deleteTrip(id: string) {
    if (!confirm('Delete this trip?')) return;
    await tripsApi.delete(id);
    if (editingId === id) resetForm();
    await loadAll();
  }

  // ── Status colors ──────────────────────────────────────────
  const statusColor: Record<string, string> = {
    Draft: '#7a92a8', Scheduled: '#2563eb', InProgress: '#0ea882',
    Completed: '#16a34a', Cancelled: '#dc2626'
  };
  const statusBg: Record<string, string> = {
    Draft: '#f0f4f8', Scheduled: '#eff6ff', InProgress: '#e6f7f2',
    Completed: '#f0fdf4', Cancelled: '#fee2e2'
  };

  // ── Filter ─────────────────────────────────────────────────
  let filterStatus = 'all';
  $: filtered = $tripList.filter(t => filterStatus === 'all' || t.status === filterStatus);

  // ── Right panel tab ────────────────────────────────────────
  let rightTab: 'map' | 'list' = 'map';

  onMount(async () => {
    await initMap();
    if (rightTab === 'map' && map) renderMapWaypoints();
  });
  onDestroy(() => { map?.remove(); map = null; });
</script>

<div class="trips-page">
  <!-- Top bar -->
  <div class="page-header">
    <h1>Trip Planner</h1>
    <div class="header-actions">
      {#if editingId}
        <span class="editing-chip">Editing trip</span>
        <button class="ghost-btn" on:click={resetForm}>← Cancel editing</button>
      {/if}
    </div>
  </div>

  <div class="body">

    <!-- ── Left: form ── -->
    <div class="form-col">
      <section class="section">
        <h2>Trip Details</h2>

        <!-- Basic info -->
        <label class="field">
          <span>Name *</span>
          <input type="text" placeholder="e.g. Morning Delivery Round" bind:value={name} />
        </label>

        <label class="field">
          <span>Description</span>
          <textarea rows="2" placeholder="Optional notes…" bind:value={description}></textarea>
        </label>

        <label class="field">
          <span>Assign Vehicle</span>
          <select bind:value={vehicleId}>
            <option value="">— unassigned —</option>
            {#each $vehicleList.filter(v => v.status === 'Idle') as v}
              <option value={v.id}>{v.licensePlate} ({v.vehicleType})</option>
            {/each}
          </select>
        </label>

        <!-- Circular toggle -->
        <label class="toggle-row">
          <div>
            <div class="toggle-label">Circular trip</div>
            <div class="toggle-hint">Vehicle returns to the starting point</div>
          </div>
          <button aria-label="Toggle circular" class="toggle-btn"
            class:on={isCircular} on:click={() => isCircular = !isCircular}
            aria-pressed={isCircular}> <span class="thumb"></span>
          </button>
        </label>
      </section>

      <!-- Waypoints -->
      <section class="section stops-section">
        <div class="stops-header">
          <h2>Stops ({waypoints.length})</h2>
          {#if !isCircular || waypoints.length === 0}
            <button class="add-stop-btn" on:click={addStop}>+ Add Stop</button>
          {/if}
        </div>

        {#if waypoints.length === 0}
          <div class="empty-stops">
            <p>Click <strong>+ Add Stop</strong> then tap the map to place it.</p>
          </div>
        {:else}
          <div class="wp-list">
            {#each waypoints as wp, i}
              <!-- svelte-ignore a11y-click-events-have-key-events -->
              <!-- svelte-ignore a11y-no-static-element-interactions -->
              <div
                      class="wp-card"
                      class:active-wp={activeWpIdx === i}
                      class:start-card={i === 0}
                      class:end-card={i === waypoints.length - 1 && waypoints.length > 1}
                      class:pending={wp.pending && activeWpIdx === i}
              >
                <div class="wp-top">
                  <span class="wp-badge"
                        class:start-b={i === 0}
                        class:end-b={i === waypoints.length - 1 && waypoints.length > 1}
                  >
                    {i === 0 ? 'START' : i === waypoints.length - 1 ? (isCircular ? 'RETURN' : 'END') : String(i)}
                  </span>
                  <input
                          class="wp-name"
                          type="text"
                          placeholder="Stop name"
                          bind:value={wp.label}
                          disabled={isCircular && i === waypoints.length - 1}
                  />
                  {#if !(isCircular && i === waypoints.length - 1)}
                    <button class="icon-btn" title="Remove stop" on:click={() => removeStop(i)}>✕</button>
                  {/if}
                </div>

                {#if wp.lat && wp.lon}
                  <!-- Coords set — show them + option to re-pick -->
                  <div class="wp-coords-row">
                    <span class="coords-val">{parseFloat(wp.lat).toFixed(5)}, {parseFloat(wp.lon).toFixed(5)}</span>
                    {#if !(isCircular && i === waypoints.length - 1)}
                      <button class="repick-btn" on:click={() => activateStop(i)} title="Re-pick on map">
                        📍 Re-pick
                      </button>
                    {/if}
                  </div>
                {:else}
                  <!-- No coords yet -->
                  {#if activeWpIdx === i}
                    <div class="map-prompt active">
                      <span class="pulse">●</span> Click anywhere on the map to set this stop's location
                    </div>
                  {:else}
                    <button class="pick-btn" on:click={() => activateStop(i)}>
                      📍 Click to pick on map
                    </button>
                  {/if}
                {/if}

                {#if !(isCircular && i === waypoints.length - 1)}
                  <div class="wp-extras">
                    <input class="wp-notes" type="text" placeholder="Notes (optional)" bind:value={wp.notes} />
                    <div class="dwell-row">
                      <label>Dwell time</label>
                      <input class="dwell-input" type="number" min="0" bind:value={wp.dwellMin} />
                      <span>min</span>
                    </div>
                  </div>
                {/if}
              </div>
            {/each}
          </div>
        {/if}

        {#if isCircular && waypoints.length >= 1}
          <p class="circular-note">↩ Route will return to starting point automatically.</p>
        {/if}
      </section>

      {#if formErr}<div class="form-error">⚠ {formErr}</div>{/if}

      <div class="form-footer">
        <button class="save-btn" disabled={saving || waypoints.length < 2} on:click={save}>
          {saving ? 'Saving…' : editingId ? '✓ Update Trip' : '✓ Create Trip'}
        </button>
        {#if editingId}
          <button class="ghost-btn" on:click={resetForm}>Cancel</button>
        {/if}
      </div>
    </div>

    <!-- ── Right: trip list ── -->
    <div class="right-col">
      <!-- Tab bar -->
      <div class="tab-bar">
        <button class="tab" class:active={rightTab === 'list'} on:click={() => rightTab = 'list'}>
          ▤ Saved Trips ({$tripList.length})
        </button>
        <button class="tab" class:active={rightTab === 'map'} on:click={() => rightTab = 'map'}>
          ◎ Map
        </button>
      </div>

      <!-- Map panel -->
      <div class="map-panel" class:hidden={rightTab !== 'map'}>
        {#if activeWpIdx >= 0}
          <div class="map-hint">
            <span class="pulse-dot"></span>
            Click on the map to place <strong>
            {waypoints[activeWpIdx]?.label || `Stop ${activeWpIdx + 1}`}
          </strong>
            <button class="cancel-pick" on:click={() => { activeWpIdx = -1; removeClickCursor(); }}>
              Cancel
            </button>
          </div>
        {/if}
        <div class="map-el" bind:this={mapEl}></div>
      </div>

      <div class="list-panel" class:hidden={rightTab !== 'list'}>
        <div class="list-toolbar">
          <select class="filter-sel" bind:value={filterStatus}>
            <option value="all">All statuses</option>
            {#each ['Draft','Scheduled','InProgress','Completed','Cancelled'] as s}
              <option value={s}>{s}</option>
            {/each}
          </select>
        </div>

      <div class="trip-list">
        {#each filtered as t (t.id)}
          <div class="trip-card" class:editing={editingId === t.id}>

            <div class="trip-top">
              <span class="trip-name">{t.name}</span>
              <span class="chip" style="color:{statusColor[t.status]};background:{statusBg[t.status]}">
                {t.status}
              </span>
            </div>

            {#if t.description}
              <p class="trip-desc">{t.description}</p>
            {/if}

            <div class="trip-meta">
              <span title="Waypoints">📍 {t.waypoints.length} stops</span>
              <span title="Distance">⟷ {t.totalDistanceKm.toFixed(1)} km</span>
              {#if t.isCircular}<span title="Circular">↩ Circular</span>{/if}
              {#if t.vehicleId}
                <span title="Vehicle">🚚 {$vehicleList.find(v => v.id === t.vehicleId)?.licensePlate ?? t.vehicleId.slice(0,8)}</span>
              {/if}
            </div>

            <!-- Compact waypoint preview -->
            <div class="wp-strip">
              {#each t.waypoints as w, i}
                <div class="wp-dot-row">
                  <span class="small-dot"
                    style="background:{i === 0 ? '#0ea882' : i === t.waypoints.length - 1 ? '#dc2626' : '#2563eb'}">
                  </span>
                  <span class="small-label">{w.label}</span>
                  <span class="wp-preview-coords">{w.coordinate.latitude.toFixed(4)}, {w.coordinate.longitude.toFixed(4)}</span>
                  {#if w.dwellMin > 0}
                    <span class="wp-dwell">{w.dwellMin}min</span>
                  {/if}
                </div>
              {/each}
            </div>

            <div class="trip-actions">
              {#if t.status === 'Draft' || t.status === 'Scheduled'}
                <button class="act start"    on:click={() => startTrip(t.id)}>▶ Start</button>
                <button class="act edit-act" on:click={() => { loadIntoForm(t); rightTab='map'; }}>✎ Edit</button>
              {:else if t.status === 'InProgress'}
                <button class="act complete" on:click={() => completeTrip(t.id)}>✓ Complete</button>
                <button class="act cancel-a" on:click={() => cancelTrip(t.id)}>✕ Cancel</button>
              {/if}
              <button class="act del" on:click={() => deleteTrip(t.id)}>✕ Delete</button>
            </div>
          </div>
        {:else}
          <div class="empty-list">No trips yet. Build one on the map!</div>
        {/each}
      </div>
      </div>
    </div>
  </div>
</div>

<style>
  /* ── Layout ── */
  .trips-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; background: var(--bg); }
  .page-header {
    display: flex; align-items: center; gap: 12px;
    padding: 12px 20px; background: var(--bg-panel);
    border-bottom: 1px solid var(--border); box-shadow: var(--shadow-sm); flex-shrink: 0;
  }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); }
  .header-actions { display: flex; align-items: center; gap: 10px; margin-left: auto; }
  .editing-chip { font-size: 11px; font-weight: 600; background: var(--accent-bg); color: var(--accent); border-radius: 6px; padding: 2px 9px; }

  .body { display: grid; grid-template-columns: 360px 1fr; flex: 1; overflow: hidden; }

  /* ── Form column ── */
  .form-col {
    border-right: 1px solid var(--border); background: var(--bg-panel);
    display: flex; flex-direction: column; overflow-y: auto; overflow-x: hidden;
  }
  .section { padding: 16px 18px; border-bottom: 1px solid var(--border); display: flex; flex-direction: column; gap: 10px; }
  .stops-section { flex: 1; }
  h2 { font-size: 10px; font-weight: 800; color: var(--text-faint); text-transform: uppercase; letter-spacing: 0.1em; }
  .stops-header { display: flex; align-items: center; justify-content: space-between; }
  .add-stop-btn {
    background: var(--accent); border: none; color: #fff;
    border-radius: 6px; padding: 5px 12px; font-family: inherit;
    font-size: 12px; font-weight: 600; cursor: pointer;
    transition: background 0.15s; box-shadow: 0 1px 4px rgba(14,168,130,0.3);
  }
  .add-stop-btn:hover { background: var(--accent-hover); }

  /* Fields */
  .field { display: flex; flex-direction: column; gap: 3px; }
  .field > span { font-size: 10px; font-weight: 700; color: var(--text-faint); text-transform: uppercase; letter-spacing: 0.07em; }
  .field input, .field select, .field textarea {
    background: var(--bg); border: 1px solid var(--border); color: var(--text);
    border-radius: 7px; padding: 7px 10px; font-family: inherit; font-size: 13px;
    outline: none; resize: none; transition: border-color 0.12s;
  }
  .field input:focus, .field select:focus, .field textarea:focus { border-color: var(--accent); }

  /* Toggle */
  .toggle-row { display: flex; align-items: center; justify-content: space-between; padding: 8px 10px; background: var(--bg-subtle); border: 1px solid var(--border); border-radius: 8px; cursor: pointer; }
  .toggle-label { font-size: 13px; font-weight: 600; color: var(--text); }
  .toggle-hint  { font-size: 11px; color: var(--text-faint); margin-top: 1px; }
  .toggle-btn { width: 40px; height: 22px; border-radius: 11px; border: none; background: var(--border); position: relative; cursor: pointer; transition: background 0.2s; flex-shrink: 0; }
  .toggle-btn.on { background: var(--accent); }
  .thumb { position: absolute; top: 3px; left: 3px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: left 0.2s; box-shadow: 0 1px 3px rgba(0,0,0,0.2); }
  .toggle-btn.on .thumb { left: 21px; }

  /* Empty stops placeholder */
  .empty-stops {
    padding: 20px 14px; text-align: center;
    border: 2px dashed var(--border); border-radius: 10px; margin: 4px 0;
  }
  .empty-stops p { font-size: 13px; color: var(--text-muted); line-height: 1.5; }

  /* Waypoint cards */
  .wp-list { display: flex; flex-direction: column; gap: 8px; padding-top: 4px; }
  .wp-card {
    border: 1.5px solid var(--border); border-radius: 10px;
    padding: 10px 12px; display: flex; flex-direction: column; gap: 7px;
    background: var(--bg-subtle); transition: border-color 0.15s, box-shadow 0.15s;
  }
  .wp-card.start-card { border-color: #86efac; background: #f0fdf4; }
  .wp-card.end-card   { border-color: #fca5a5; background: #fff1f2; }
  .wp-card.active-wp  { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-bg); }
  .wp-card.pending    { border-color: var(--accent-warn); box-shadow: 0 0 0 3px var(--accent-warn-bg); }

  .wp-top { display: flex; align-items: center; gap: 7px; }
  .wp-badge { font-size: 9px; font-weight: 800; border-radius: 4px; padding: 2px 6px; background: var(--border); color: var(--text-muted); flex-shrink: 0; letter-spacing: 0.08em; }
  .start-b { background: #dcfce7; color: #15803d; }
  .end-b   { background: #fee2e2; color: #b91c1c; }
  .wp-name { flex: 1; border: 1px solid var(--border); background: var(--bg); color: var(--text); border-radius: 6px; padding: 4px 8px; font-family: inherit; font-size: 12px; font-weight: 600; outline: none; }
  .wp-name:focus { border-color: var(--accent); }
  .wp-name:disabled { opacity: 0.5; }
  .icon-btn { background: transparent; border: 1px solid var(--border); color: var(--accent-err); border-radius: 5px; padding: 3px 7px; font-size: 11px; cursor: pointer; transition: background 0.1s; flex-shrink: 0; }
  .icon-btn:hover { background: var(--accent-err-bg); }

  /* Map interaction */
  .wp-coords-row { display: flex; align-items: center; gap: 8px; }
  .coords-val { font-size: 11px; font-family: 'DM Mono', monospace; color: var(--text-muted); flex: 1; }
  .repick-btn { font-size: 11px; color: var(--accent); background: var(--accent-bg); border: none; border-radius: 5px; padding: 3px 8px; cursor: pointer; font-family: inherit; font-weight: 600; white-space: nowrap; }
  .repick-btn:hover { background: #c7f0e4; }
  .pick-btn { font-size: 12px; color: var(--accent); background: var(--accent-bg); border: 1px dashed var(--accent); border-radius: 7px; padding: 6px 10px; cursor: pointer; font-family: inherit; font-weight: 600; text-align: left; transition: background 0.12s; }
  .pick-btn:hover { background: #c7f0e4; }
  .map-prompt { font-size: 12px; color: var(--accent-warn); background: var(--accent-warn-bg); border-radius: 6px; padding: 6px 10px; display: flex; align-items: center; gap: 6px; }
  .map-prompt.active { animation: fadeIn 0.2s ease; }
  @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
  .pulse { font-size: 14px; animation: blink 1s infinite; }
  @keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.2; } }

  /* Extras */
  .wp-extras { display: flex; flex-direction: column; gap: 5px; }
  .wp-notes { border: 1px solid var(--border); background: var(--bg); color: var(--text-muted); border-radius: 6px; padding: 5px 8px; font-family: inherit; font-size: 12px; outline: none; }
  .wp-notes:focus { border-color: var(--accent); color: var(--text); }
  .dwell-row { display: flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-muted); }
  .dwell-input { width: 56px; border: 1px solid var(--border); background: var(--bg); color: var(--text); border-radius: 6px; padding: 4px 8px; font-family: inherit; font-size: 12px; outline: none; }

  .circular-note { font-size: 11px; color: var(--accent); text-align: center; padding: 4px 0; }
  .form-error { margin: 0 18px; color: var(--accent-err); font-size: 12px; padding: 7px 10px; background: var(--accent-err-bg); border-radius: 7px; }

  .form-footer { padding: 14px 18px; display: flex; gap: 8px; flex-shrink: 0; }
  .save-btn {
    flex: 1; background: var(--accent); border: none; color: #fff;
    border-radius: 8px; padding: 10px; font-family: inherit; font-size: 13px;
    font-weight: 600; cursor: pointer; box-shadow: 0 2px 6px rgba(14,168,130,0.25);
    transition: background 0.15s;
  }
  .save-btn:hover:not(:disabled) { background: var(--accent-hover); }
  .save-btn:disabled { opacity: 0.45; cursor: not-allowed; }
  .ghost-btn { background: transparent; border: 1px solid var(--border); color: var(--text-muted); border-radius: 8px; padding: 10px 14px; font-family: inherit; font-size: 13px; font-weight: 600; cursor: pointer; transition: border-color 0.12s; }
  .ghost-btn:hover { border-color: var(--text-muted); color: var(--text); }

  /* ── Right column ── */
  .right-col { display: flex; flex-direction: column; overflow: hidden; }

  .tab-bar { display: flex; border-bottom: 1px solid var(--border); background: var(--bg-panel); flex-shrink: 0; }
  .tab {
    padding: 10px 18px; font-family: inherit; font-size: 13px; font-weight: 500;
    color: var(--text-muted); background: transparent; border: none; border-bottom: 2.5px solid transparent;
    cursor: pointer; transition: color 0.12s, border-color 0.12s;
  }
  .tab:hover { color: var(--text); }
  .tab.active { color: var(--accent); border-bottom-color: var(--accent); font-weight: 700; }

  /* Map */
  .map-panel { flex: 1; display: flex; flex-direction: column; overflow: hidden; position: relative; }
  .map-panel.hidden { display: none; }
  .map-hint {
    position: absolute; top: 10px; left: 50%; transform: translateX(-50%);
    z-index: 1000; background: #fff; border: 1.5px solid var(--accent);
    border-radius: 20px; padding: 6px 14px 6px 10px;
    font-size: 12px; font-weight: 600; color: var(--text);
    box-shadow: 0 2px 10px rgba(0,0,0,0.15); white-space: nowrap;
    display: flex; align-items: center; gap: 8px;
  }
  .pulse-dot {
    width: 10px; height: 10px; border-radius: 50%;
    background: var(--accent); flex-shrink: 0;
    animation: blink 0.8s infinite;
  }
  .cancel-pick { background: transparent; border: none; color: var(--accent-err); font-size: 11px; font-weight: 700; cursor: pointer; margin-left: 4px; font-family: inherit; }
  .map-el { flex: 1; min-height: 0; }

  /* Trip list */
  .list-panel { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
  .list-panel.hidden { display: none; }
  .list-toolbar { padding: 10px 16px; border-bottom: 1px solid var(--border); background: var(--bg-panel); flex-shrink: 0; }
  .filter-sel { background: var(--bg); border: 1px solid var(--border); color: var(--text); border-radius: 7px; padding: 5px 10px; font-family: inherit; font-size: 12px; outline: none; }
  .trip-list { flex: 1; overflow-y: auto; padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; }

  .trip-card {
    background: var(--bg-panel); border: 1px solid var(--border); border-radius: 10px;
    padding: 13px 15px; display: flex; flex-direction: column; gap: 7px;
    box-shadow: var(--shadow-sm); transition: box-shadow 0.15s, border-color 0.15s;
  }
  .trip-card:hover { box-shadow: var(--shadow-md); }
  .trip-card.editing { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-bg); }
  .trip-top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
  .trip-name { font-size: 14px; font-weight: 700; color: var(--text); }
  .chip { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; flex-shrink: 0; }
  .trip-desc { font-size: 12px; color: var(--text-muted); margin: 0; line-height: 1.4; }
  .trip-meta { display: flex; gap: 12px; font-size: 12px; color: var(--text-muted); flex-wrap: wrap; }

  .wp-strip { display: flex; flex-direction: column; gap: 3px; padding: 7px 9px; background: var(--bg-subtle); border-radius: 7px; }
  .wp-dot-row { display: flex; align-items: center; gap: 7px; }

  .wp-preview-label { font-size: 12px; font-weight: 600; color: var(--text); flex: 1; }
  .wp-preview-coords { font-size: 11px; color: var(--text-muted); font-family: 'DM Mono', monospace; }
  .wp-dwell { font-size: 10px; color: var(--accent-warn); background: var(--accent-warn-bg); border-radius: 4px; padding: 1px 5px; flex-shrink: 0; }

  .small-dot  { width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0; }
  .small-label { font-size: 11px; color: var(--text-muted); }

  .trip-actions { display: flex; gap: 7px; flex-wrap: wrap; }
  .act {
    border: 1px solid; border-radius: 6px; padding: 4px 11px;
    font-family: inherit; font-size: 11px; font-weight: 600; cursor: pointer; background: transparent; transition: background 0.12s;
  }
  .act.start    { border-color: var(--accent);      color: var(--accent);         }
  .act.start:hover    { background: var(--accent-bg); }
  .act.complete { border-color: #16a34a;             color: #16a34a;               }
  .act.complete:hover { background: #f0fdf4; }
  .act.cancel-a { border-color: var(--accent-err);   color: var(--accent-err);     }
  .act.cancel-a:hover { background: var(--accent-err-bg); }
  .act.edit-act { border-color: var(--accent-warn);  color: var(--accent-warn);    }
  .act.edit-act:hover { background: var(--accent-warn-bg); }
  .act.del      { border-color: var(--border);       color: var(--text-faint);     }
  .act.del:hover      { border-color: var(--accent-err); color: var(--accent-err); background: var(--accent-err-bg); }

  .empty-list { color: var(--text-faint); font-size: 13px; text-align: center; padding: 24px; }
</style>