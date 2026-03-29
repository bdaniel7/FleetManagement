<script lang="ts">
  import { vehicleList, loading, loadAll } from '../stores/fleet';
  import { vehicles as vehiclesApi } from '$lib/api';
  import type { Vehicle } from '$lib/api';

  let filterStatus = 'all';
  let filterQuery  = '';
  let sortKey: keyof Vehicle = 'licensePlate';
  let sortAsc  = true;
  let deleting: string | null = null;

  $: filtered = $vehicleList
    //.filter(v => filterStatus === 'all' || v.status === filterStatus)
    .filter(v => {
      if (!filterQuery) return true;
      const q = filterQuery.toLowerCase();
      return v.licensePlate.toLowerCase().includes(q) || v.vehicleType.toLowerCase().includes(q);
    })
    .sort((a, b) => {
      const av = a[sortKey] as string | number;
      const bv = b[sortKey] as string | number;
      const cmp = av < bv ? -1 : av > bv ? 1 : 0;
      return sortAsc ? cmp : -cmp;
    });

  function sort(key: keyof Vehicle) {
    if (sortKey === key) sortAsc = !sortAsc;
    else { sortKey = key; sortAsc = true; }
  }

  async function deleteVehicle(id: string) {
    if (!confirm('Remove this vehicle from the fleet?')) return;
    deleting = id;
    try {
      await vehiclesApi.delete(id);
      await loadAll();
    } finally {
      deleting = null;
    }
  }

  async function changeStatus(id: string, status: string) {
    await vehiclesApi.updateStatus(id, status);
    await loadAll();
  }

  const fuelColor = (pct: number) =>
    pct < 10 ? '#f43f5e' : pct < 25 ? '#f59e0b' : '#22d3a5';

  const statusColors: Record<string, string> = {
    Idle: '#3b82f6', 'En Route': '#22d3a5',
    Maintenance: '#f59e0b', Charging: '#a78bfa', 'Out Of Service': '#f43f5e'
  };

  const statuses = ['Idle', 'En Route', 'Maintenance', 'Charging', 'Out Of Service'];
  let k : unknown;
  const key = k as keyof Vehicle;

  const onStatusChange = (vId: string) => (e: Event) => {
    changeStatus(vId, (e.target as HTMLSelectElement).value);
  };
</script>

<div class="vehicles-page">
  <div class="page-header">
    <span class="title-icon">▣</span>
    <h1>Vehicles</h1>
    <span class="count-badge">{filtered.length} / {$vehicleList.length}</span>

    <div class="filters">
      <input
        class="search-input"
        type="search"
        placeholder="Search plate or type…"
        bind:value={filterQuery}
      />
      <select class="status-select" bind:value={filterStatus}>
        <option value="all">All Statuses</option>
        {#each statuses as s}
          <option value={s}>{s}</option>
        {/each}
      </select>
    </div>
  </div>

  {#if $loading}
    <div class="loading">Loading…</div>
  {:else}
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            {#each [['licensePlate','Plate'], ['vehicleType','Type'], ['status','Status'], ['fuelLevelPct','Fuel'], ['speedKmh','Speed'], ['maxPayloadKg','Payload']] as [k, label]}
              <th on:click={() => sort(key)} class:sorted={sortKey === key}>
                {label}
                {#if sortKey === k}<span class="sort-arrow">{sortAsc ? '↑' : '↓'}</span>{/if}
              </th>
            {/each}
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {#each filtered as v (v.id)}
            <tr>
              <td class="plate-cell">{v.licensePlate}</td>
              <td class="type-cell">{v.vehicleType}</td>
              <td>
                <select
                  class="inline-status"
                  value={v.status}
                  style="color:{statusColors[v.status]}"
                  on:change={onStatusChange(v.id)}
                >
                  {#each statuses as s}
                    <option value={s}>{s}</option>
                  {/each}
                </select>
              </td>
              <td>
                <div class="fuel-cell">
                  <div class="mini-bar-track">
                    <div class="mini-bar-fill" style="width:{v.fuelLevelPct}%;background:{fuelColor(v.fuelLevelPct)}"></div>
                  </div>
                  <span style="color:{fuelColor(v.fuelLevelPct)}">{v.fuelLevelPct.toFixed(0)}%</span>
                </div>
              </td>
              <td class="num-cell">{v.speedKmh.toFixed(0)} km/h</td>
              <td class="num-cell">{v.maxPayloadKg.toFixed(0)} kg</td>
              <td>
                <button
                  class="del-btn"
                  disabled={deleting === v.id}
                  on:click={() => deleteVehicle(v.id)}
                >
                  {deleting === v.id ? '…' : '✕'}
                </button>
              </td>
            </tr>
          {:else}
            <tr>
              <td colspan="7" class="empty-cell">No vehicles match the current filter.</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

<style>
  .vehicles-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; }
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
  .count-badge { background: #111d2c; color: #4a637e; border-radius: 8px; padding: 2px 10px; font-size: 11px; margin-right: auto; }
  .filters { display: flex; gap: 10px; }
  .search-input, .status-select {
    background: #0d1420;
    border: 1px solid #1e2d45;
    color: #c8d6e8;
    border-radius: 6px;
    padding: 6px 12px;
    font-family: inherit;
    font-size: 12px;
    outline: none;
  }
  .search-input:focus, .status-select:focus { border-color: #22d3a5; }

  .loading { padding: 40px; color: #4a637e; font-size: 13px; text-align: center; }
  .table-wrap { flex: 1; overflow-y: auto; }

  table { width: 100%; border-collapse: collapse; }
  thead th {
    background: #0a1118;
    color: #4a637e;
    font-size: 10px;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    padding: 10px 16px;
    text-align: left;
    border-bottom: 1px solid #1e2d45;
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
  }
  thead th:hover { color: #6b84a0; }
  thead th.sorted { color: #22d3a5; }
  .sort-arrow { margin-left: 4px; }

  tbody tr { border-bottom: 1px solid #111d2c; transition: background 0.1s; }
  tbody tr:hover { background: #0d1420; }
  td { padding: 10px 16px; font-size: 12px; color: #c8d6e8; }

  .plate-cell { font-weight: 700; letter-spacing: 0.05em; color: #e2eaf4; }
  .type-cell  { color: #6b84a0; }
  .num-cell   { color: #a8bdd4; }
  .empty-cell { text-align: center; color: #4a637e; padding: 40px; }

  .fuel-cell { display: flex; align-items: center; gap: 8px; }
  .mini-bar-track { width: 60px; height: 5px; background: #111d2c; border-radius: 3px; overflow: hidden; }
  .mini-bar-fill  { height: 100%; border-radius: 3px; transition: width 0.5s; }

  .inline-status {
    background: transparent;
    border: 1px solid #1e2d45;
    border-radius: 5px;
    padding: 3px 8px;
    font-family: inherit;
    font-size: 11px;
    font-weight: 600;
    cursor: pointer;
    outline: none;
  }
  .inline-status:hover { border-color: #2a4060; }

  .del-btn {
    background: transparent;
    border: 1px solid #1e2d45;
    color: #f43f5e;
    border-radius: 5px;
    padding: 3px 8px;
    font-size: 12px;
    cursor: pointer;
    transition: background 0.15s;
  }
  .del-btn:hover:not(:disabled) { background: #2d0f18; }
  .del-btn:disabled { opacity: 0.4; cursor: not-allowed; }
</style>
