<script lang="ts">
    import {vehicleList, loading, loadAll} from '../stores/fleet';
    import {vehicles as vehiclesApi} from '$lib/api';
    import type {Vehicle} from '$lib/api';

    let filterStatus = 'all';
    let filterQuery = '';
    let sortKey: keyof Vehicle = 'status';
    let sortAsc = true;
    let deleting: string | null = null;

    $: filtered = $vehicleList
        .filter(v => filterStatus === 'all' || v.status === filterStatus)
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
        else {
            sortKey = key;
            sortAsc = true;
        }
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

    const fuelColor = (p: number) => p < 10 ? '#dc2626' : p < 25 ? '#d97706' : '#0ea882';
    const statusColors: Record<string, string> = {
        Idle: '#2563eb', 'En Route': '#0ea882', Maintenance: '#d97706', Charging: '#7c3aed', 'Out Of Service': '#dc2626'
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
        <div class="header-left">
            <h1>Vehicles</h1>
            <span class="count-badge">{filtered.length} / {$vehicleList.length}</span>
        </div>
        <div class="filters">
            <input class="search-input" type="search" placeholder="Search plate or type…" bind:value={filterQuery}/>
            <select class="filter-select" bind:value={filterStatus}>
                <option value="all">All Statuses</option>
                {#each statuses as s}
                    <option value={s}>{s}</option>
                {/each}
            </select>
        </div>
    </div>

    {#if $loading}
        <div class="loading">Loading vehicles…</div>
    {:else}
        <!-- Desktop table view -->
        <div class="table-wrap">
            <table class="desktop-table">
                <thead>
                <tr>
                    {#each [['licensePlate', 'Plate'], ['vehicleType', 'Type'], ['status', 'Status'], ['fuelLevelPct', 'Fuel'], ['speedKmh', 'Speed'], ['maxPayloadKg', 'Payload']] as [k, label]}
                        <th on:click={() => sort(key)} class:sorted={sortKey === key}>
                            {label}{sortKey === k ? (sortAsc ? ' ↑' : ' ↓') : ''}
                        </th>
                    {/each}
                    <th></th>
                </tr>
                </thead>
                <tbody>
                {#each filtered as v (v.id)}
                    <tr>
                        <td class="plate-cell">{v.licensePlate}</td>
                        <td class="type-cell">{v.vehicleType}</td>
                        <td>
                            <select class="inline-status" value={v.status}
                                    style="color:{statusColors[v.status]};border-color:{statusColors[v.status]}44"
                                    on:change={onStatusChange(v.id)}>
                                {#each statuses as s}
                                    <option value={s}>{s}</option>
                                {/each}
                            </select>
                        </td>
                        <td>
                            <div class="fuel-cell">
                                <div class="mini-bar-track">
                                    <div class="mini-bar-fill"
                                         style="width:{v.fuelLevelPct}%;background:{fuelColor(v.fuelLevelPct)}"></div>
                                </div>
                                <span class="fuel-num"
                                      style="color:{fuelColor(v.fuelLevelPct)}">{v.fuelLevelPct.toFixed(0)}%</span>
                            </div>
                        </td>
                        <td class="num-cell">{v.speedKmh.toFixed(0)} km/h</td>
                        <td class="num-cell">{v.maxPayloadKg.toFixed(0)} kg</td>
                        <td>
                            <button class="del-btn" disabled={deleting === v.id} on:click={() => deleteVehicle(v.id)}>
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

            <!-- Mobile card view -->
            <div class="card-grid">
                {#each filtered as v (v.id)}
                    <div class="vehicle-card">
                        <div class="card-header">
                            <span class="plate-cell">{v.licensePlate}</span>
                            <button class="del-btn" disabled={deleting === v.id} on:click={() => deleteVehicle(v.id)}>
                                {deleting === v.id ? '…' : '✕'}
                            </button>
                        </div>
                        <div class="card-body">
                            <div class="card-row">
                                <span class="card-label">Type</span>
                                <span class="card-value">{v.vehicleType}</span>
                            </div>
                            <div class="card-row">
                                <span class="card-label">Status</span>
                                <select class="inline-status" value={v.status}
                                        style="color:{statusColors[v.status]};border-color:{statusColors[v.status]}44"
                                        on:change={onStatusChange(v.id)}>
                                    {#each statuses as s}
                                        <option value={s}>{s}</option>
                                    {/each}
                                </select>
                            </div>
                            <div class="card-row">
                                <span class="card-label">Fuel</span>
                                <div class="fuel-cell">
                                    <div class="mini-bar-track">
                                        <div class="mini-bar-fill"
                                             style="width:{v.fuelLevelPct}%;background:{fuelColor(v.fuelLevelPct)}"></div>
                                    </div>
                                    <span class="fuel-num" style="color:{fuelColor(v.fuelLevelPct)}">{v.fuelLevelPct.toFixed(0)}%</span>
                                </div>
                            </div>
                            <div class="card-row">
                                <span class="card-label">Speed</span>
                                <span class="num-cell">{v.speedKmh.toFixed(0)} km/h</span>
                            </div>
                            <div class="card-row">
                                <span class="card-label">Payload</span>
                                <span class="num-cell">{v.maxPayloadKg.toFixed(0)} kg</span>
                            </div>
                        </div>
                    </div>
                {:else}
                    <div class="empty-cell">No vehicles match the current filter.</div>
                {/each}
            </div>
        </div>
    {/if}
</div>

<style>
    .vehicles-page {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        background: var(--bg);
    }

    .page-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 14px 24px;
        background: var(--bg-panel);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
        box-shadow: var(--shadow-sm);
    }

    .header-left {
        display: flex;
        align-items: center;
        gap: 10px;
    }

    h1 {
        font-size: 17px;
        font-weight: 700;
        color: var(--text);
    }

    .count-badge {
        background: var(--bg-hover);
        color: var(--text-faint);
        border-radius: 8px;
        padding: 2px 10px;
        font-size: 11px;
        font-weight: 600;
    }

    .filters {
        display: flex;
        gap: 8px;
    }

    .search-input, .filter-select {
        background: var(--bg);
        border: 1px solid var(--border);
        color: var(--text);
        border-radius: 7px;
        padding: 6px 12px;
        font-family: inherit;
        font-size: 13px;
        outline: none;
        transition: border-color 0.12s;
    }

    .search-input:focus, .filter-select:focus {
        border-color: var(--accent);
    }

    .search-input::-webkit-search-cancel-button,
    .search-input::-ms-clear {
        appearance: none;
        width: 16px;
        height: 16px;
        cursor: pointer;
    }

    .loading {
        padding: 40px;
        color: var(--text-faint);
        text-align: center;
        font-size: 13px;
    }

    .table-wrap {
        flex: 1;
        overflow-y: auto;
    }

    table {
        width: 100%;
        border-collapse: collapse;
    }

    thead th {
        background: var(--bg-subtle);
        color: var(--text-muted);
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.07em;
        text-transform: uppercase;
        padding: 10px 16px;
        text-align: left;
        border-bottom: 1px solid var(--border);
        cursor: pointer;
        user-select: none;
        white-space: nowrap;
    }

    thead th:hover {
        color: var(--text);
    }

    thead th.sorted {
        color: var(--accent);
    }

    tbody tr {
        border-bottom: 1px solid var(--border);
        transition: background 0.1s;
    }

    tbody tr:hover {
        background: var(--bg-hover);
    }

    td {
        padding: 10px 16px;
        font-size: 13px;
        color: var(--text);
    }

    .plate-cell {
        font-weight: 700;
        font-family: 'DM Mono', monospace;
        letter-spacing: 0.04em;
    }

    .type-cell {
        color: var(--text-muted);
    }

    .num-cell {
        color: var(--text-muted);
        font-family: 'DM Mono', monospace;
        font-size: 12px;
    }

    .empty-cell {
        text-align: center;
        color: var(--text-faint);
        padding: 40px;
    }

    .fuel-cell {
        display: flex;
        align-items: center;
        gap: 8px;
    }

    .mini-bar-track {
        width: 60px;
        height: 6px;
        background: var(--bg-hover);
        border-radius: 3px;
        overflow: hidden;
    }

    .mini-bar-fill {
        height: 100%;
        border-radius: 3px;
        transition: width 0.5s;
    }

    .fuel-num {
        font-size: 12px;
        font-weight: 600;
        font-family: 'DM Mono', monospace;
    }

    .inline-status {
        background: var(--bg-subtle);
        border: 1px solid;
        border-radius: 6px;
        padding: 3px 8px;
        font-family: inherit;
        font-size: 12px;
        font-weight: 600;
        cursor: pointer;
        outline: none;
    }

    .del-btn {
        background: transparent;
        border: 1px solid var(--border);
        color: var(--accent-err);
        border-radius: 6px;
        padding: 4px 8px;
        font-size: 12px;
        cursor: pointer;
        transition: background 0.12s, border-color 0.12s;
    }

    .del-btn:hover:not(:disabled) {
        background: var(--accent-err-bg);
        border-color: var(--accent-err);
    }

    .del-btn:disabled {
        opacity: 0.4;
        cursor: not-allowed;
    }

    /* ── Mobile Card View ── */
    .desktop-table { display: table; }
    .card-grid { display: none; }

    @media (max-width: 768px) {
        .vehicles-page { padding: 0; }

        .page-header {
            flex-wrap: wrap;
            padding: 12px 16px;
            gap: 8px;
        }

        .header-left { gap: 8px; }
        h1 { font-size: 16px; }

        .filters { flex-wrap: wrap; width: 100%; }
        .search-input, .filter-select { flex: 1; min-width: 120px; }

        .desktop-table { display: none; }
        .card-grid {
            display: flex;
            flex-direction: column;
            gap: 12px;
            padding: 16px;
        }

        .vehicle-card {
            background: var(--bg-panel);
            border: 1px solid var(--border);
            border-radius: 10px;
            overflow: hidden;
        }

        .card-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 12px 14px;
            background: var(--bg-subtle);
            border-bottom: 1px solid var(--border);
        }

        .card-body {
            padding: 12px 14px;
            display: flex;
            flex-direction: column;
            gap: 10px;
        }

        .card-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 12px;
        }

        .card-label {
            font-size: 11px;
            font-weight: 600;
            color: var(--text-muted);
            text-transform: uppercase;
            letter-spacing: 0.06em;
        }

        .card-value {
            font-size: 13px;
            color: var(--text);
        }
    }

    @media (max-width: 480px) {
        .search-input, .filter-select {
            width: 100%;
        }

        .filters {
            flex-direction: column;
        }
    }
</style>
