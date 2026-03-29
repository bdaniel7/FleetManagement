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
    const parsed = waypoints.map(w => ({
      latitude: parseFloat(w.lat), longitude: parseFloat(w.lon)
    }));
    if (parsed.some(p => isNaN(p.latitude) || isNaN(p.longitude))) {
      planError = 'All waypoints must have valid coordinates';
      return;
    }
    planning  = true;
    planError = '';
    try {
      await routesApi.plan({ vehicleId, waypoints: parsed, algorithm, priority });
      await loadAll();
    } catch (e) {
      planError = (e as Error).message;
    } finally {
      planning = false;
    }
  }

  // ── Route table ───────────────────────────────────────────
  async function activateRoute(id: string) { await routesApi.activate(id); await loadAll(); }
  async function completeRoute(id: string) { await routesApi.complete(id); await loadAll(); }
  async function cancelRoute(id: string)   { await routesApi.cancel(id, 'Manual cancellation'); await loadAll(); }

  const statusColor: Record<string, string> = {
    Planned: '#60a5fa', Active: '#22d3a5',
    Completed: '#4a637e', Cancelled: '#f43f5e', Rerouting: '#f59e0b'
  };

  const algoDesc: Record<string, string> = {
    AStar:       'A* — heuristic, fastest for sparse graphs',
    Dijkstra:    'Dijkstra — optimal for moderate graphs',
    BellmanFord: 'Bellman-Ford — handles negative weights'
  };

  let filterStatus = 'all';
  $: filteredRoutes = $routeList.filter(r =>
    filterStatus === 'all' || r.status === filterStatus
  );
</script>

<div class="routes-page">
  <div class="page-header">
    <span class="title-icon">◈</span>
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
        <p class="field-hint">{algoDesc[algorithm]}</p>
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
            <div class="waypoint-row">
              <span class="wp-num">{i + 1}</span>
              <input class="coord-input" type="text" placeholder="Lat" bind:value={wp.lat} />
              <input class="coord-input" type="text" placeholder="Lon" bind:value={wp.lon} />
              {#if waypoints.length > 2}
                <button class="wp-del" on:click={() => removeWaypoint(i)}>✕</button>
              {/if}
            </div>
          {/each}
          <button class="add-wp-btn" on:click={addWaypoint}>+ Add Waypoint</button>
        </div>
      </div>

      {#if planError}
        <div class="form-error">⚠ {planError}</div>
      {/if}

      <button
        class="plan-btn"
        on:click={planRoute}
        disabled={planning}
      >
        {planning ? 'Computing…' : '⬡ Compute Route'}
      </button>
    </div>

    <!-- Right: Route list -->
    <div class="route-list-panel">
      <div class="list-header">
        <h2>Routes</h2>
        <select class="status-filter" bind:value={filterStatus}>
          <option value="all">All</option>
          {#each ['Planned','Active','Completed','Cancelled','Rerouting'] as s}
            <option value={s}>{s}</option>
          {/each}
        </select>
      </div>

      <div class="route-list">
        {#each filteredRoutes as r (r.id)}
          <div class="route-card">
            <div class="route-top">
              <span class="route-status" style="color:{statusColor[r.status]}">● {r.status}</span>
              <span class="route-algo">{r.algorithm}</span>
              <span class="route-priority priority-{r.priority.toLowerCase()}">{r.priority}</span>
            </div>
            <div class="route-stats">
              <span>📍 {r.totalDistanceKm.toFixed(1)} km</span>
              <span>⏱ {r.estimatedDurationMin} min</span>
            </div>
            <div class="route-vehicle">Vehicle: <code>{r.vehicleId.slice(0,8)}…</code></div>
            <div class="route-actions">
              {#if r.status === 'Planned'}
                <button class="act-btn activate" on:click={() => activateRoute(r.id)}>Activate</button>
                <button class="act-btn cancel" on:click={() => cancelRoute(r.id)}>Cancel</button>
              {:else if r.status === 'Active'}
                <button class="act-btn complete" on:click={() => completeRoute(r.id)}>Complete</button>
                <button class="act-btn cancel" on:click={() => cancelRoute(r.id)}>Cancel</button>
              {/if}
            </div>
          </div>
        {:else}
          <div class="empty-routes">No routes match the filter.</div>
        {/each}
      </div>
    </div>
  </div>
</div>

<style>
  .routes-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; }
  .page-header {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 16px 24px;
    border-bottom: 1px solid #1e2d45;
    flex-shrink: 0;
  }
  .title-icon { font-size: 24px; color: #22d3a5; }
  h1 { font-size: 18px; font-weight: 700; color: #e2eaf4; letter-spacing: 0.04em; }

  .split {
    display: grid;
    grid-template-columns: 340px 1fr;
    flex: 1;
    overflow: hidden;
  }

  /* Plan panel */
  .plan-panel {
    border-right: 1px solid #1e2d45;
    padding: 20px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  h2 { font-size: 13px; color: #6b84a0; letter-spacing: 0.08em; text-transform: uppercase; }

  .field { display: flex; flex-direction: column; gap: 5px; }
  .field > span { font-size: 11px; color: #6b84a0; letter-spacing: 0.06em; text-transform: uppercase; }
  .field select, .field input {
    background: #0d1420;
    border: 1px solid #1e2d45;
    color: #c8d6e8;
    border-radius: 6px;
    padding: 7px 10px;
    font-family: inherit;
    font-size: 12px;
    outline: none;
    width: 100%;
  }
  .field select:focus, .field input:focus { border-color: #22d3a5; }
  .field-hint { font-size: 10px; color: #4a637e; margin-top: 2px; }

  .waypoints { display: flex; flex-direction: column; gap: 6px; }
  .waypoint-row { display: flex; align-items: center; gap: 6px; }
  .wp-num { font-size: 10px; color: #4a637e; width: 16px; text-align: right; flex-shrink: 0; }
  .coord-input { flex: 1; }
  .wp-del {
    background: transparent;
    border: 1px solid #1e2d45;
    color: #f43f5e;
    border-radius: 4px;
    padding: 4px 7px;
    cursor: pointer;
    font-size: 11px;
  }
  .add-wp-btn {
    background: transparent;
    border: 1px dashed #1e2d45;
    color: #4a637e;
    border-radius: 6px;
    padding: 6px;
    font-family: inherit;
    font-size: 11px;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .add-wp-btn:hover { border-color: #22d3a5; color: #22d3a5; }

  .form-error { color: #f43f5e; font-size: 11px; }

  .plan-btn {
    background: #0f2040;
    border: 1px solid #22d3a5;
    color: #22d3a5;
    border-radius: 8px;
    padding: 10px 16px;
    font-family: inherit;
    font-size: 13px;
    font-weight: 700;
    letter-spacing: 0.05em;
    cursor: pointer;
    transition: background 0.15s;
  }
  .plan-btn:hover:not(:disabled) { background: #133050; }
  .plan-btn:disabled { opacity: 0.5; cursor: not-allowed; }

  /* Route list panel */
  .route-list-panel { display: flex; flex-direction: column; overflow: hidden; }
  .list-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px 20px;
    border-bottom: 1px solid #1e2d45;
    flex-shrink: 0;
  }
  .status-filter {
    background: #0d1420;
    border: 1px solid #1e2d45;
    color: #c8d6e8;
    border-radius: 6px;
    padding: 5px 10px;
    font-family: inherit;
    font-size: 11px;
  }

  .route-list { flex: 1; overflow-y: auto; padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; }

  .route-card {
    background: #0d1420;
    border: 1px solid #1e2d45;
    border-radius: 10px;
    padding: 14px 16px;
    display: flex;
    flex-direction: column;
    gap: 7px;
    transition: border-color 0.15s;
  }
  .route-card:hover { border-color: #2a4060; }

  .route-top { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  .route-status { font-size: 12px; font-weight: 700; }
  .route-algo   { font-size: 10px; color: #4a637e; background: #111d2c; border-radius: 4px; padding: 2px 7px; }
  .route-priority {
    font-size: 10px;
    font-weight: 700;
    border-radius: 4px;
    padding: 2px 7px;
    margin-left: auto;
  }
  .priority-normal    { background: #111d2c; color: #6b84a0; }
  .priority-high      { background: #2d1f0a; color: #f59e0b; }
  .priority-emergency { background: #2d0f18; color: #f43f5e; }
  .priority-low       { background: #111d2c; color: #4a637e; }

  .route-stats { display: flex; gap: 16px; font-size: 12px; color: #6b84a0; }
  .route-vehicle { font-size: 11px; color: #4a637e; }
  .route-vehicle code { color: #a8bdd4; font-family: inherit; }

  .route-actions { display: flex; gap: 8px; }
  .act-btn {
    background: transparent;
    border: 1px solid;
    border-radius: 5px;
    padding: 4px 12px;
    font-family: inherit;
    font-size: 11px;
    font-weight: 600;
    cursor: pointer;
    transition: background 0.15s;
  }
  .act-btn.activate { border-color: #22d3a5; color: #22d3a5; }
  .act-btn.activate:hover { background: #0f2a20; }
  .act-btn.complete { border-color: #60a5fa; color: #60a5fa; }
  .act-btn.complete:hover { background: #0f1d35; }
  .act-btn.cancel   { border-color: #f43f5e; color: #f43f5e; }
  .act-btn.cancel:hover { background: #2d0f18; }

  .empty-routes { color: #4a637e; font-size: 12px; padding: 20px; text-align: center; }
</style>
