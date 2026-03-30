<script lang="ts">
  import { routeList, vehicleList, loadAll } from '../stores/fleet';
  import { routes as routesApi } from '$lib/api';
  import type { Route } from '$lib/api';

  // ── Plan form ─────────────────────────────────────────────
  let vehicleId = '';
  let algorithm: 'AStar' | 'Dijkstra' | 'BellmanFord' = 'AStar';
  let priority: 'Low' | 'Normal' | 'High' | 'Emergency' = 'Normal';
  let waypoints: { lat: number; lon: number }[] = [
    { lat: 52.5200, lon: 13.4050 },
    { lat: 53.5511, lon: 9.9937  }
  ];
  let planning  = false;
  let planError = '';

  $: vehiclesById = $vehicleList.reduce<Record<string, string>>(
          (acc, v) => {
            acc[v.id.toString()] = `${v.licensePlate} (${v.vehicleType})`;
            return acc;
          },
          {}
  );

  function addWaypoint() {
    waypoints = [...waypoints, { lat: 0.0, lon: 0.0 }];
  }
  function removeWaypoint(i: number) {
    waypoints = waypoints.filter((_, idx) => idx !== i);
  }

  async function planRoute() {
    if (!vehicleId) {
      planError = 'Select a vehicle first'; return;
    }
    const parsed = waypoints.map(w => ({ latitude: w.lat, longitude: w.lon }));
    if (parsed.some(p => isNaN(p.latitude) || isNaN(p.longitude))) {
      planError = 'All waypoints need valid coordinates'; return;
    }
    planning = true; planError = '';
    try { await routesApi.plan({ vehicleId, waypoints: parsed, algorithm, priority }); await loadAll(); }
    catch (e) { planError = (e as Error).message; }
    finally { planning = false; }
  }

  // ── Edit waypoints ─────────────────────────────────────────
  let editingRoute: Route | null = null;
  let editWaypoints: { lat: number; lon: number }[] = [];
  let editAlgorithm: string = 'AStar';
  let saving  = false;
  let saveError = '';

  function startEdit(route: Route) {
    editingRoute = route;
    editAlgorithm = route.algorithm;
    saveError = '';
    // Pre-populate from existing waypoints if the API returns them,
    // otherwise fall back to two empty slots so the user can enter new ones.
    editWaypoints = route.waypoints.map(w => ({ lat: w.coordinate.latitude,
                                                lon: w.coordinate.longitude }));
  }

  function cancelEdit() {
    editingRoute = null; editWaypoints = []; saveError = '';
  }

  function addEditWaypoint() {
    editWaypoints = [...editWaypoints, { lat: 0.0, lon: 0.0 }];
  }
  function removeEditWaypoint(i: number) {
    editWaypoints = editWaypoints.filter((_, idx) => idx !== i);
  }

  async function saveWaypoints() {
    if (!editingRoute) return;
    const parsed = editWaypoints.map(w => ({ latitude: w.lat, longitude: w.lon }));
    if (parsed.some(p => isNaN(p.latitude) || isNaN(p.longitude))) {
      saveError = 'All waypoints need valid coordinates'; return;
    }
    if (parsed.length < 2) {
      saveError = 'At least 2 waypoints are required'; return;
    }
    saving = true; saveError = '';
    try {
      await routesApi.updateWaypoints(editingRoute.id, parsed, editAlgorithm);
      await loadAll();
      cancelEdit();
    } catch (e) {
      saveError = (e as Error).message;
    } finally {
      saving = false;
    }
  }

  // ── Route table ───────────────────────────────────────────
  async function activateRoute(id: string) { await routesApi.activate(id); await loadAll(); }
  async function completeRoute(id: string) { await routesApi.complete(id); await loadAll(); }
  async function cancelRoute(id: string)   { await routesApi.cancel(id, 'Manual cancellation'); await loadAll(); }

  const canEdit = (r: Route) => r.status === 'Planned' || r.status === 'Active';

  const statusColor: Record<string, string> = {
    Planned: '#2563eb', Active: '#0ea882', Completed: '#7a92a8', Cancelled: '#dc2626', Rerouting: '#d97706'
  };
  const statusBg: Record<string, string> = {
    Planned: '#eff6ff', Active: '#e6f7f2', Completed: '#f0f4f8', Cancelled: '#fee2e2', Rerouting: '#fef3c7'
  };
  const algoDesc: Record<string, string> = {
    AStar:       'A* — heuristic-guided, best for large sparse graphs',
    Dijkstra:    'Dijkstra — optimal cost, moderate-density graphs',
    BellmanFord: 'Bellman-Ford — handles negative edge weights'
  };
  let filterStatus = 'all';
  $: filteredRoutes = $routeList.filter(r => filterStatus === 'all' || r.status === filterStatus);
</script>

<div class="routes-page">
  <div class="page-header">
    <h1>Route Planner</h1>
  </div>

  <div class="split">
    <!-- Left: Plan form -->
    <div class="plan-panel">
      {#if editingRoute}
        <!-- ── Edit waypoints mode ── -->
        <div class="panel-heading">
          <h2>Edit Waypoints</h2>
          <button class="text-btn" on:click={cancelEdit}>← Back</button>
        </div>

        <div class="edit-meta">
          <span class="meta-label">Route</span>
          <code>{editingRoute.id.slice(0,8)}…</code>
          <span class="status-chip"
                style="color:{statusColor[editingRoute.status]};background:{statusBg[editingRoute.status]}">
            {editingRoute.status}
          </span>
        </div>

        <label class="field">
          <span>Algorithm</span>
          <select bind:value={editAlgorithm}>
            <option value="AStar">A* (recommended)</option>
            <option value="Dijkstra">Dijkstra</option>
            <option value="BellmanFord">Bellman-Ford</option>
          </select>
          <p class="hint">{algoDesc[editAlgorithm]}</p>
        </label>

        <div class="field">
          <span>Waypoints ({editWaypoints.length})</span>
          <div class="waypoints">
            {#each editWaypoints as wp, i}
              <div class="wp-row">
                <span class="wp-num">{i + 1}</span>
                <input class="coord" type="text" placeholder="Latitude"  bind:value={wp.lat} />
                <input class="coord" type="text" placeholder="Longitude" bind:value={wp.lon} />
                {#if editWaypoints.length > 2}
                  <button class="wp-del" on:click={() => removeEditWaypoint(i)} title="Remove">✕</button>
                {/if}
              </div>
            {/each}
            <button class="add-wp" on:click={addEditWaypoint}>+ Add Waypoint</button>
          </div>
        </div>

        {#if saveError}<div class="form-error">⚠ {saveError}</div>{/if}

        <div class="edit-actions">
          <button class="plan-btn" on:click={saveWaypoints} disabled={saving}>
            {saving ? 'Saving…' : '✓ Save Waypoints'}
          </button>
          <button class="secondary-btn" on:click={cancelEdit}>Cancel</button>
        </div>

      {:else}
        <!-- ── Plan new route mode ── -->
      <h2>Plan New Route</h2>

      <label class="field">
        <span>Vehicle</span>
        <select bind:value={vehicleId}>
          <option value="">— select idle vehicle —</option>
          {#each $vehicleList.filter(v => v.status === 'Idle') as v}
            <option value={v.id}>{v.licensePlate} ({v.vehicleType})</option>
          {/each}
        </select>
      </label>

      <label class="field">
        <span>Algorithm</span>
        <select bind:value={algorithm}>
          <option value="AStar">A* (recommended)</option>
          <option value="Dijkstra">Dijkstra</option>
          <option value="BellmanFord">Bellman-Ford</option>
        </select>
        <p class="hint">{algoDesc[algorithm]}</p>
      </label>

      <label class="field">
        <span>Priority</span>
        <select bind:value={priority}>
          <option value="Low">Low</option>
          <option value="Normal">Normal</option>
          <option value="High">High</option>
          <option value="Emergency">Emergency</option>
        </select>
      </label>

      <div class="field">
        <span>Waypoints ({waypoints.length})</span>
        <div class="waypoints">
          {#each waypoints as wp, i}
            <div class="wp-row">
              <span class="wp-num">{i+1}</span>
              <input class="coord" type="text" placeholder="Latitude" bind:value={wp.lat} />
              <input class="coord" type="text" placeholder="Longitude" bind:value={wp.lon} />
              {#if waypoints.length > 2}
                <button class="wp-del" on:click={() => removeWaypoint(i)} title="Remove">✕</button>
              {/if}
            </div>
          {/each}
          <button class="add-wp" on:click={addWaypoint}>+ Add Waypoint</button>
        </div>
      </div>

      {#if planError}<div class="form-error">⚠ {planError}</div>{/if}

      <button class="plan-btn" on:click={planRoute} disabled={planning}>
        {planning ? 'Computing…' : '⬡ Compute Route'}
      </button>
     {/if}
    </div>

    <!-- Right: route list -->
    <div class="list-panel">
      <div class="list-header">
        <h2>Routes <span class="route-count">{filteredRoutes.length}</span></h2>
        <select class="filter-select" bind:value={filterStatus}>
          <option value="all">All</option>
          {#each ['Planned','Active','Completed','Cancelled','Rerouting'] as s}<option value={s}>{s}</option>{/each}
        </select>
      </div>

      <div class="route-list">
        {#each filteredRoutes as r (r.id)}
          <div class="route-card" class:editing={editingRoute?.id === r.id}>
            <div class="route-top">
              <span class="route-status" style="color:{statusColor[r.status]};background:{statusBg[r.status]}">
                {r.status}
              </span>
              <span class="route-algo">{r.algorithm}</span>
              <span class="route-priority prio-{r.priority.toLowerCase()}">{r.priority}</span>
            </div>
            <div class="route-stats">
              <span>📍 {r.totalDistanceKm.toFixed(1)} km</span>
              <span>⏱ {r.estimatedDurationMin} min</span>
            </div>
            <div class="route-id">
              Route <code>{r.id}</code>
            </div>
            <div class="route-id">Vehicle <code>{vehiclesById[r.vehicleId]}</code></div>
            <div class="route-actions">
              {#if r.status === 'Planned'}
                <button class="act activate" on:click={() => activateRoute(r.id)}>Activate</button>
                <button class="act cancel"   on:click={() => cancelRoute(r.id)}>Cancel</button>
              {:else if r.status === 'Active'}
                <button class="act complete" on:click={() => completeRoute(r.id)}>Complete</button>
                <button class="act cancel"   on:click={() => cancelRoute(r.id)}>Cancel</button>
              {/if}
              {#if canEdit(r)}
                <button
                        class="act edit"
                        class:active-edit={editingRoute?.id === r.id}
                        on:click={() => editingRoute?.id === r.id ? cancelEdit() : startEdit(r)}
                >
                  {editingRoute?.id === r.id ? '✕ Close' : '✎ Edit Waypoints'}
                </button>
              {/if}
            </div>
          </div>
        {:else}
          <div class="empty-routes">No routes match the current filter.</div>
        {/each}
      </div>
    </div>
  </div>
</div>

<style>
  .routes-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; background: var(--bg); }
  .page-header {
    padding: 14px 24px;
    background: var(--bg-panel);
    border-bottom: 1px solid var(--border);
    box-shadow: var(--shadow-sm);
    flex-shrink: 0;
  }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); }

  .split { display: grid; grid-template-columns: 340px 1fr; flex: 1; overflow: hidden; }

  /* Plan panel */
  .plan-panel {
    border-right: 1px solid var(--border);
    padding: 20px;
    overflow-y: auto;
    display: flex; flex-direction: column; gap: 14px;
    background: var(--bg-panel);
  }
  .panel-heading { display: flex; align-items: center; justify-content: space-between; }
  h2 { font-size: 11px; font-weight: 700; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }

  .text-btn {
    background: transparent; border: none; color: var(--accent);
    font-family: inherit; font-size: 12px; font-weight: 600; cursor: pointer; padding: 0;
  }
  .text-btn:hover { text-decoration: underline; }

  .edit-meta {
    display: flex; align-items: center; gap: 8px;
    padding: 10px 12px; background: var(--bg-subtle);
    border: 1px solid var(--border); border-radius: 8px;
    font-size: 12px;
  }
  .meta-label { color: var(--text-faint); }
  .edit-meta code { font-family: 'DM Mono', monospace; color: var(--text); }
  .status-chip { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 7px; }

  .field { display: flex; flex-direction: column; gap: 4px; }
  .field > span { font-size: 11px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .field select, .coord {
    background: var(--bg);
    border: 1px solid var(--border);
    color: var(--text);
    border-radius: 7px;
    padding: 7px 10px;
    font-family: inherit;
    font-size: 13px;
    outline: none;
    width: 100%;
    transition: border-color 0.12s;
  }
  .field select:focus, .coord:focus { border-color: var(--accent); }
  .hint { font-size: 11px; color: var(--text-faint); margin-top: 2px; }

  .waypoints { display: flex; flex-direction: column; gap: 6px; }
  .wp-row { display: flex; align-items: center; gap: 6px; }
  .wp-num { font-size: 11px; color: var(--text-faint); width: 16px; text-align: right; flex-shrink: 0; font-family: 'DM Mono', monospace; }
  .coord  { flex: 1; }
  .wp-del {
    background: transparent; border: 1px solid var(--border); color: var(--accent-err);
    border-radius: 5px; padding: 4px 7px; font-size: 11px; cursor: pointer;
    transition: background 0.12s;
  }
  .wp-del:hover { background: var(--accent-err-bg); }
  .add-wp {
    background: transparent; border: 1px dashed var(--border); color: var(--text-faint);
    border-radius: 7px; padding: 6px; font-family: inherit; font-size: 12px; cursor: pointer;
    transition: border-color 0.12s, color 0.12s;
  }
  .add-wp:hover { border-color: var(--accent); color: var(--accent); }

  .form-error { color: var(--accent-err); font-size: 12px; padding: 6px 10px; background: var(--accent-err-bg); border-radius: 6px; }
  .edit-actions { display: flex; gap: 8px; }
  .plan-btn {
    background: var(--accent);
    border: none;
    color: #fff;
    border-radius: 8px;
    padding: 10px 16px;
    font-family: inherit;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    transition: background 0.15s, box-shadow 0.15s;
    box-shadow: 0 2px 6px rgba(14,168,130,0.25);
    flex: 1;
  }
  .plan-btn:hover:not(:disabled) { background: var(--accent-hover); box-shadow: 0 4px 10px rgba(14,168,130,0.3); }
  .plan-btn:disabled { opacity: 0.5; cursor: not-allowed; }

  .secondary-btn {
    background: transparent; border: 1px solid var(--border); color: var(--text-muted);
    border-radius: 8px; padding: 10px 14px; font-family: inherit;
    font-size: 13px; font-weight: 600; cursor: pointer;
    transition: border-color 0.12s, color 0.12s;
  }
  .secondary-btn:hover { border-color: var(--text-muted); color: var(--text); }

  /* Route list panel */
  .list-panel { display: flex; flex-direction: column; overflow: hidden; background: var(--bg); }
  .list-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 20px;
    border-bottom: 1px solid var(--border);
    background: var(--bg-panel);
    flex-shrink: 0;
  }

  .route-count {
    display: inline-flex; align-items: center; justify-content: center;
    background: var(--bg-hover); color: var(--text-faint);
    border-radius: 8px; padding: 1px 8px; font-size: 11px;
    font-weight: 600; margin-left: 6px; text-transform: none; letter-spacing: 0;
  }

  .filter-select {
    background: var(--bg); border: 1px solid var(--border); color: var(--text);
    border-radius: 7px; padding: 5px 10px; font-family: inherit; font-size: 12px; outline: none;
  }

  .route-list { flex: 1; overflow-y: auto; padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; }

  .route-card {
    background: var(--bg-panel);
    border: 1px solid var(--border);
    border-radius: 10px;
    padding: 14px 16px;
    display: flex; flex-direction: column; gap: 7px;
    box-shadow: var(--shadow-sm);
    transition: box-shadow 0.15s;
  }
  .route-card:hover { box-shadow: var(--shadow-md); }
  .route-card.editing { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-bg), var(--shadow-md); }
  .route-top  { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .route-status { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; }
  .route-algo   { font-size: 11px; color: var(--text-muted); background: var(--bg-hover); border-radius: 5px; padding: 2px 7px; }
  .route-priority { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; margin-left: auto; }
  .prio-normal    { background: var(--bg-hover);          color: var(--text-muted); }
  .prio-low       { background: var(--bg-hover);          color: var(--text-faint); }
  .prio-high      { background: var(--accent-warn-bg);    color: var(--accent-warn); }
  .prio-emergency { background: var(--accent-err-bg);     color: var(--accent-err);  }

  .route-stats { display: flex; gap: 16px; font-size: 12px; color: var(--text-muted); }
  .route-id    { display: flex; align-items: center; gap: 10px; font-size: 11px; color: var(--text-faint); }
  .route-id code { font-family: 'DM Mono', monospace; color: var(--text-muted); }
  .vehicle-id  { color: var(--text-faint); font-family: 'DM Mono', monospace; }

  .route-actions { display: flex; gap: 8px; flex-wrap: wrap}
  .act {
    border: 1px solid; border-radius: 6px; padding: 4px 12px;
    font-family: inherit; font-size: 11px; font-weight: 600; cursor: pointer; background: transparent;
    transition: background 0.12s;
  }
  .act.activate { border-color: var(--accent);      color: var(--accent); }
  .act.activate:hover { background: var(--accent-bg); }
  .act.complete { border-color: var(--accent-blue);  color: var(--accent-blue); }
  .act.complete:hover { background: #eff6ff; }
  .act.cancel   { border-color: var(--accent-err);   color: var(--accent-err); }
  .act.cancel:hover   { background: var(--accent-err-bg); }
  .act.edit     { border-color: var(--accent-warn);  color: var(--accent-warn); margin-left: auto; }
  .act.edit:hover     { background: var(--accent-warn-bg); }
  .act.edit.active-edit { background: var(--accent-warn-bg); }

  .empty-routes { color: var(--text-faint); font-size: 13px; padding: 20px; text-align: center; }
</style>
