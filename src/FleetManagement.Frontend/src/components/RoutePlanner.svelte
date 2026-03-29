<script lang="ts">
  import { routeList, vehicleList, loadAll } from '../stores/fleet';
  import { routes as routesApi } from '$lib/api';
  import type { Route } from '$lib/api';

  // ── Plan form ─────────────────────────────────────────────
  let vehicleId = '';
  let algorithm: 'AStar' | 'Dijkstra' | 'BellmanFord' = 'AStar';
  let priority: 'Low' | 'Normal' | 'High' | 'Emergency' = 'Normal';
  let waypoints: { lat: string; lon: string }[] = [
    { lat: '44.4268', lon: '26.1025' },
    { lat: '44.4350', lon: '26.0950' }
  ];
  let planning  = false;
  let planError = '';

  function addWaypoint()      { waypoints = [...waypoints, { lat: '', lon: '' }]; }
  function removeWaypoint(i: number) { waypoints = waypoints.filter((_, idx) => idx !== i); }

  async function planRoute() {
    if (!vehicleId) { planError = 'Select a vehicle first'; return; }
    const parsed = waypoints.map(w => ({ latitude: parseFloat(w.lat), longitude: parseFloat(w.lon) }));
    if (parsed.some(p => isNaN(p.latitude) || isNaN(p.longitude))) { planError = 'All waypoints need valid coordinates'; return; }
    planning = true; planError = '';
    try { await routesApi.plan({ vehicleId, waypoints: parsed, algorithm, priority }); await loadAll(); }
    catch (e) { planError = (e as Error).message; }
    finally { planning = false; }
  }

  // ── Route table ───────────────────────────────────────────
  async function activateRoute(id: string) { await routesApi.activate(id); await loadAll(); }
  async function completeRoute(id: string) { await routesApi.complete(id); await loadAll(); }
  async function cancelRoute(id: string)   { await routesApi.cancel(id, 'Manual cancellation'); await loadAll(); }

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
      <h2>Plan New Route</h2>

      <label class="field">
        <span>Vehicle</span>
        <select bind:value={vehicleId}>
          <option value="">— select vehicle —</option>
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
              <input class="coord" type="text" placeholder="Lat" bind:value={wp.lat} />
              <input class="coord" type="text" placeholder="Lon" bind:value={wp.lon} />
              {#if waypoints.length > 2}
                <button class="wp-del" on:click={() => removeWaypoint(i)}>✕</button>
              {/if}
            </div>
          {/each}
          <button class="add-wp" on:click={addWaypoint}>+ Add Waypoint</button>
        </div>
      </div>

      {#if planError}<div class="form-error">{planError}</div>{/if}

      <button class="plan-btn" on:click={planRoute} disabled={planning}>
        {planning ? 'Computing…' : '⬡ Compute Route'}
      </button>
    </div>

    <!-- Right: route list -->
    <div class="list-panel">
      <div class="list-header">
        <h2>Routes</h2>
        <select class="filter-select" bind:value={filterStatus}>
          <option value="all">All</option>
          {#each ['Planned','Active','Completed','Cancelled','Rerouting'] as s}<option value={s}>{s}</option>{/each}
        </select>
      </div>

      <div class="route-list">
        {#each filteredRoutes as r (r.id)}
          <div class="route-card">
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
            <div class="route-id">Route <code>{r.id.slice(0,8)}…</code></div>
            <div class="route-actions">
              {#if r.status === 'Planned'}
                <button class="act activate" on:click={() => activateRoute(r.id)}>Activate</button>
                <button class="act cancel"   on:click={() => cancelRoute(r.id)}>Cancel</button>
              {:else if r.status === 'Active'}
                <button class="act complete" on:click={() => completeRoute(r.id)}>Complete</button>
                <button class="act cancel"   on:click={() => cancelRoute(r.id)}>Cancel</button>
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

  .split { display: grid; grid-template-columns: 320px 1fr; flex: 1; overflow: hidden; }

  /* Plan panel */
  .plan-panel {
    border-right: 1px solid var(--border);
    padding: 20px;
    overflow-y: auto;
    display: flex; flex-direction: column; gap: 14px;
    background: var(--bg-panel);
  }
  h2 { font-size: 11px; font-weight: 700; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }

  .field { display: flex; flex-direction: column; gap: 4px; }
  .field > span { font-size: 11px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .field select, .field input, .coord {
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
  .field select:focus, .field input:focus, .coord:focus { border-color: var(--accent); }
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

  .form-error { color: var(--accent-err); font-size: 12px; }

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
  }
  .plan-btn:hover:not(:disabled) { background: var(--accent-hover); box-shadow: 0 4px 10px rgba(14,168,130,0.3); }
  .plan-btn:disabled { opacity: 0.5; cursor: not-allowed; }

  /* Route list panel */
  .list-panel { display: flex; flex-direction: column; overflow: hidden; background: var(--bg); }
  .list-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 20px;
    border-bottom: 1px solid var(--border);
    background: var(--bg-panel);
    flex-shrink: 0;
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
  .route-top  { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .route-status { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; }
  .route-algo   { font-size: 11px; color: var(--text-muted); background: var(--bg-hover); border-radius: 5px; padding: 2px 7px; }
  .route-priority { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; margin-left: auto; }
  .prio-normal    { background: var(--bg-hover);          color: var(--text-muted); }
  .prio-low       { background: var(--bg-hover);          color: var(--text-faint); }
  .prio-high      { background: var(--accent-warn-bg);    color: var(--accent-warn); }
  .prio-emergency { background: var(--accent-err-bg);     color: var(--accent-err);  }

  .route-stats { display: flex; gap: 16px; font-size: 12px; color: var(--text-muted); }
  .route-id    { font-size: 11px; color: var(--text-faint); }
  .route-id code { font-family: 'DM Mono', monospace; color: var(--text-muted); }

  .route-actions { display: flex; gap: 8px; }
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

  .empty-routes { color: var(--text-faint); font-size: 13px; padding: 20px; text-align: center; }
</style>
