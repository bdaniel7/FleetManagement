<script lang="ts">
  import { onMount } from 'svelte';
  import { tripList, vehicleList, loadAll } from '../stores/fleet';
  import { trips as tripsApi } from '$lib/api';
  import type { Trip, TripWaypoint, CreateTripRequest } from '$lib/api';

  // ── Form state ─────────────────────────────────────────────
  type WpForm = { label: string; lat: string; lon: string; notes: string; dwellMin: string };

  let name        = '';
  let description = '';
  let vehicleId   = '';
  let isCircular  = false;
  let waypoints: WpForm[] = [
    { label: 'Start', lat: '', lon: '', notes: '', dwellMin: '0' },
    { label: 'End',   lat: '', lon: '', notes: '', dwellMin: '0' }
  ];

  let saving  = false;
  let formErr = '';

  // ── Editing an existing trip ───────────────────────────────
  let editingId: string | null = null;

  function newForm() {
    editingId   = null;
    name        = '';
    description = '';
    vehicleId   = '';
    isCircular  = false;
    waypoints   = [
      { label: 'Start', lat: '', lon: '', notes: '', dwellMin: '0' },
      { label: 'End',   lat: '', lon: '', notes: '', dwellMin: '0' }
    ];
    formErr = '';
  }

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
      dwellMin:String(w.dwellMin)
    }));
    formErr = '';
  }

  // ── Waypoint management ────────────────────────────────────
  function addWp() {
    waypoints = [...waypoints, { label: '', lat: '', lon: '', notes: '', dwellMin: '0' }];
  }
  function removeWp(i: number) {
    if (waypoints.length <= 2) return;
    waypoints = waypoints.filter((_, idx) => idx !== i);
  }
  function moveUp(i: number) {
    if (i === 0) return;
    const arr = [...waypoints];
    [arr[i-1], arr[i]] = [arr[i], arr[i-1]];
    waypoints = arr;
  }
  function moveDown(i: number) {
    if (i === waypoints.length - 1) return;
    const arr = [...waypoints];
    [arr[i], arr[i+1]] = [arr[i+1], arr[i]];
    waypoints = arr;
  }

  // ── Copy first to last for circular ───────────────────────
  $: if (isCircular && waypoints.length >= 2) {
    const first = waypoints[0];
    const last  = waypoints[waypoints.length - 1];
    if (first.lat && first.lon && (last.lat !== first.lat || last.lon !== first.lon)) {
      waypoints = [
        ...waypoints.slice(0, -1),
        { label: first.label + ' (return)', lat: first.lat, lon: first.lon, notes: '', dwellMin: '0' }
      ];
    }
  }

  // ── Save / update ──────────────────────────────────────────
  async function save() {
    formErr = '';
    if (!name.trim()) { formErr = 'Trip name is required'; return; }
    if (waypoints.length < 2) { formErr = 'At least 2 waypoints required'; return; }

    const parsed = waypoints.map((w, i) => ({
      order:    i,
      label:    w.label.trim() || `Stop ${i + 1}`,
      lat:      parseFloat(w.lat),
      lon:      parseFloat(w.lon),
      notes:    w.notes,
      dwellMin: parseInt(w.dwellMin) || 0
    }));

    if (parsed.some(p => isNaN(p.lat) || isNaN(p.lon))) {
      formErr = 'All waypoints need valid lat/lon coordinates'; return;
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
      newForm();
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
    await loadAll();
    if (editingId === id) newForm();
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
  $: filtered = $tripList.filter(t =>
    filterStatus === 'all' || t.status === filterStatus
  );

  onMount(newForm);
</script>

<div class="trips-page">
  <div class="page-header">
    <h1>Trip Planner</h1>
    <button class="new-btn" on:click={newForm}>+ New Trip</button>
  </div>

  <div class="split">

    <!-- ── Left: form ── -->
    <div class="form-panel">
      <div class="form-title">
        {#if editingId}
          <h2>Editing Trip</h2>
          <button class="text-btn" on:click={newForm}>← New</button>
        {:else}
          <h2>Define New Trip</h2>
        {/if}
      </div>

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
        <div class="toggle-info">
          <span class="toggle-label">Circular trip</span>
          <span class="toggle-hint">Return to the starting point</span>
        </div>
        <button aria-label="Toggle circular"
          class="toggle-btn"
          class:on={isCircular}
          on:click={() => isCircular = !isCircular}
          aria-pressed={isCircular}>
          <span class="thumb"></span>
        </button>
      </label>

      <!-- Waypoints -->
      <div class="field">
        <span>Waypoints ({waypoints.length}){isCircular ? ' — circular' : ''}</span>
        <div class="wp-list">
          {#each waypoints as wp, i}
            <div class="wp-card" class:first={i === 0} class:last={i === waypoints.length - 1}>
              <div class="wp-header">
                <span class="wp-badge"
                  class:start-badge={i === 0}
                  class:end-badge={i === waypoints.length - 1}
                >
                  {i === 0 ? 'START' : i === waypoints.length - 1 ? (isCircular ? 'RETURN' : 'END') : String(i)}
                </span>
                <input
                  class="wp-label-input"
                  type="text"
                  placeholder="Stop name"
                  bind:value={wp.label}
                  disabled={isCircular && i === waypoints.length - 1}
                />
                <div class="wp-btns">
                  {#if i > 0 && !(isCircular && i === waypoints.length - 1)}
                    <button class="icon-btn" title="Move up" on:click={() => moveUp(i)}>↑</button>
                  {/if}
                  {#if i < waypoints.length - 1 && !(isCircular && i === waypoints.length - 1)}
                    <button class="icon-btn" title="Move down" on:click={() => moveDown(i)}>↓</button>
                  {/if}
                  {#if waypoints.length > 2 && !(i === 0) && !(isCircular && i === waypoints.length - 1)}
                    <button class="icon-btn danger" title="Remove" on:click={() => removeWp(i)}>✕</button>
                  {/if}
                </div>
              </div>
              <div class="wp-coords">
                <input class="coord" type="text" placeholder="Latitude"
                  bind:value={wp.lat} disabled={isCircular && i === waypoints.length - 1}
                />
                <input
                  class="coord" type="text" placeholder="Longitude" bind:value={wp.lon}
                  disabled={isCircular && i === waypoints.length - 1}
                />
              </div>
                <div>
                    <span class="wp-badge dwell-time">Stop duration (minutes): </span>
                    <input class="dwell" type="number" min="0" placeholder="Dwell (min)"
                        bind:value={wp.dwellMin} title="Stop duration in minutes"/>
                </div>
              {#if !(isCircular && i === waypoints.length - 1)}
                <input
                  class="wp-notes"
                  type="text"
                  placeholder="Notes (optional)"
                  bind:value={wp.notes}
                />
              {/if}
            </div>
          {/each}

          {#if !isCircular}
            <button class="add-wp" on:click={addWp}>+ Add Stop</button>
          {:else}
            <p class="circular-note">
              ↩ The last stop mirrors the start point automatically.
            </p>
          {/if}
        </div>
      </div>

      {#if formErr}<div class="form-error">⚠ {formErr}</div>{/if}

      <div class="form-actions">
        <button class="save-btn" disabled={saving} on:click={save}>
          {saving ? 'Saving…' : editingId ? '✓ Update Trip' : '✓ Create Trip'}
        </button>
        {#if editingId}
          <button class="cancel-edit-btn" on:click={newForm}>Cancel</button>
        {/if}
      </div>
    </div>

    <!-- ── Right: trip list ── -->
    <div class="list-panel">
      <div class="list-header">
        <h2>Trips <span class="count">{filtered.length}</span></h2>
        <select class="filter-sel" bind:value={filterStatus}>
          <option value="all">All</option>
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

            <!-- Inline waypoint preview -->
            <div class="wp-preview">
              {#each t.waypoints as w, i}
                <div class="wp-preview-row">
                  <span class="wp-dot"
                    style="background:{i === 0 ? '#0ea882' : i === t.waypoints.length - 1 ? '#dc2626' : '#2563eb'}">
                  </span>
                  <span class="wp-preview-label">{w.label}</span>
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
                <button class="act edit-act" on:click={() => loadIntoForm(t)}>✎ Edit</button>
                <button class="act del"      on:click={() => deleteTrip(t.id)}>✕</button>
              {:else if t.status === 'InProgress'}
                <button class="act complete" on:click={() => completeTrip(t.id)}>✓ Complete</button>
                <button class="act cancel-a" on:click={() => cancelTrip(t.id)}>✕ Cancel</button>
              {:else}
                <button class="act del" on:click={() => deleteTrip(t.id)}>✕ Delete</button>
              {/if}
            </div>
          </div>
        {:else}
          <div class="empty">No trips match the filter.</div>
        {/each}
      </div>
    </div>
  </div>
</div>

<style>
  .trips-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; background: var(--bg); }
  .page-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 24px; background: var(--bg-panel);
    border-bottom: 1px solid var(--border); box-shadow: var(--shadow-sm); flex-shrink: 0;
  }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); }
  .new-btn {
    background: var(--accent); border: none; color: #fff;
    border-radius: 7px; padding: 7px 14px; font-family: inherit;
    font-size: 12px; font-weight: 600; cursor: pointer;
    transition: background 0.15s;
  }
  .new-btn:hover { background: var(--accent-hover); }

  .split { display: grid; grid-template-columns: 380px 1fr; flex: 1; overflow: hidden; }

  /* ── Form panel ── */
  .form-panel {
    border-right: 1px solid var(--border); padding: 18px 20px;
    overflow-y: auto; display: flex; flex-direction: column; gap: 13px;
    background: var(--bg-panel);
  }
  .form-title { display: flex; align-items: center; justify-content: space-between; }
  h2 { font-size: 11px; font-weight: 700; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }
  .text-btn { background: transparent; border: none; color: var(--accent); font-family: inherit; font-size: 12px; font-weight: 600; cursor: pointer; padding: 0; }
  .text-btn:hover { text-decoration: underline; }

  .field { display: flex; flex-direction: column; gap: 4px; }
  .field > span { font-size: 11px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .field input, .field select, .field textarea, .coord, .dwell, .wp-notes, .wp-label-input {
    background: var(--bg); border: 1px solid var(--border); color: var(--text);
    border-radius: 7px; padding: 7px 10px; font-family: inherit; font-size: 13px;
    outline: none; transition: border-color 0.12s; resize: none;
  }
  .field input:focus, .field select:focus, .field textarea:focus,
  .coord:focus, .dwell:focus, .wp-notes:focus, .wp-label-input:focus { border-color: var(--accent); }
  .field input:disabled, .coord:disabled, .wp-label-input:disabled { opacity: 0.5; cursor: not-allowed; }

  /* Circular toggle */
  .toggle-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: 10px 12px; background: var(--bg-subtle);
    border: 1px solid var(--border); border-radius: 9px; cursor: pointer;
  }
  .toggle-info { display: flex; flex-direction: column; gap: 1px; }
  .toggle-label { font-size: 13px; font-weight: 600; color: var(--text); }
  .toggle-hint  { font-size: 11px; color: var(--text-faint); }
  .toggle-btn {
    width: 40px; height: 22px; border-radius: 11px; border: none;
    background: var(--border); cursor: pointer; position: relative;
    transition: background 0.2s; flex-shrink: 0;
  }
  .toggle-btn.on { background: var(--accent); }
  .thumb {
    position: absolute; top: 3px; left: 3px;
    width: 16px; height: 16px; border-radius: 50%; background: #fff;
    transition: left 0.2s; box-shadow: 0 1px 3px rgba(0,0,0,0.2);
  }
  .toggle-btn.on .thumb { left: 21px; }

  /* Waypoints */
  .wp-list { display: flex; flex-direction: column; gap: 8px; }
  .wp-card {
    background: var(--bg-subtle); border: 1px solid var(--border);
    border-radius: 9px; padding: 10px 12px; display: flex; flex-direction: column; gap: 6px;
  }
  .wp-card.first { border-color: #86efac; background: #f0fdf4; }
  .wp-card.last  { border-color: #fca5a5; background: #fff1f2; }
  .wp-header { display: flex; align-items: center; gap: 7px; }
  .wp-badge {
    font-size: 9px; font-weight: 800; border-radius: 4px; padding: 2px 6px;
    background: var(--border); color: var(--text-muted); flex-shrink: 0;
    letter-spacing: 0.08em;
  }

  .dwell-time {font-size: 11px;}
  .start-badge { background: #dcfce7; color: #15803d; }
  .end-badge   { background: #fee2e2; color: #b91c1c; }
  .wp-label-input { flex: 1; padding: 5px 8px; font-size: 12px; font-weight: 600; }
  .wp-btns { display: flex; gap: 3px; flex-shrink: 0; }
  .icon-btn {
    background: transparent; border: 1px solid var(--border); color: var(--text-muted);
    border-radius: 5px; padding: 2px 6px; font-size: 11px; cursor: pointer;
    transition: background 0.1s;
  }
  .icon-btn:hover { background: var(--bg-hover); }
  .icon-btn.danger { color: var(--accent-err); }
  .icon-btn.danger:hover { background: var(--accent-err-bg); border-color: var(--accent-err); }
  .wp-coords { display: flex; gap: 6px; }
  .coord  { flex: 1; padding: 5px 8px; font-size: 12px; font-family: 'DM Mono', monospace; width: 24px}
  .dwell  { width: 80px; padding: 5px 8px; font-size: 12px; }
  .wp-notes { font-size: 12px; padding: 5px 8px; color: var(--text-muted); }

  .add-wp {
    background: transparent; border: 1px dashed var(--border);
    color: var(--text-faint); border-radius: 8px; padding: 7px;
    font-family: inherit; font-size: 12px; cursor: pointer;
    transition: border-color 0.12s, color 0.12s;
  }
  .add-wp:hover { border-color: var(--accent); color: var(--accent); }
  .circular-note { font-size: 11px; color: var(--accent); margin: 2px 0; }

  .form-error { color: var(--accent-err); font-size: 12px; padding: 7px 10px; background: var(--accent-err-bg); border-radius: 7px; }
  .form-actions { display: flex; gap: 8px; }
  .save-btn {
    flex: 1; background: var(--accent); border: none; color: #fff;
    border-radius: 8px; padding: 10px; font-family: inherit;
    font-size: 13px; font-weight: 600; cursor: pointer;
    box-shadow: 0 2px 6px rgba(14,168,130,0.25); transition: background 0.15s;
  }
  .save-btn:hover:not(:disabled) { background: var(--accent-hover); }
  .save-btn:disabled { opacity: 0.5; cursor: not-allowed; }
  .cancel-edit-btn {
    background: transparent; border: 1px solid var(--border); color: var(--text-muted);
    border-radius: 8px; padding: 10px 14px; font-family: inherit; font-size: 13px; font-weight: 600; cursor: pointer;
  }

  /* ── List panel ── */
  .list-panel { display: flex; flex-direction: column; overflow: hidden; background: var(--bg); }
  .list-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 20px; border-bottom: 1px solid var(--border);
    background: var(--bg-panel); flex-shrink: 0;
  }
  .count { background: var(--bg-hover); color: var(--text-faint); border-radius: 8px; padding: 1px 8px; font-size: 11px; font-weight: 600; margin-left: 6px; text-transform: none; letter-spacing: 0; }
  .filter-sel {
    background: var(--bg); border: 1px solid var(--border); color: var(--text);
    border-radius: 7px; padding: 5px 10px; font-family: inherit; font-size: 12px; outline: none;
  }

  .trip-list { flex: 1; overflow-y: auto; padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; }

  .trip-card {
    background: var(--bg-panel); border: 1px solid var(--border); border-radius: 11px;
    padding: 14px 16px; display: flex; flex-direction: column; gap: 8px;
    box-shadow: var(--shadow-sm); transition: box-shadow 0.15s, border-color 0.15s;
  }
  .trip-card:hover { box-shadow: var(--shadow-md); }
  .trip-card.editing { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-bg), var(--shadow-md); }

  .trip-top  { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .trip-name { font-size: 14px; font-weight: 700; color: var(--text); }
  .chip { font-size: 11px; font-weight: 700; border-radius: 5px; padding: 2px 8px; flex-shrink: 0; }
  .trip-desc { font-size: 12px; color: var(--text-muted); margin: 0; line-height: 1.4; }
  .trip-meta { display: flex; gap: 14px; flex-wrap: wrap; font-size: 12px; color: var(--text-muted); }

  /* Waypoint preview strip */
  .wp-preview { display: flex; flex-direction: column; gap: 4px; padding: 8px 10px; background: var(--bg-subtle); border-radius: 8px; }
  .wp-preview-row { display: flex; align-items: center; gap: 8px; }
  .wp-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
  .wp-preview-label { font-size: 12px; font-weight: 600; color: var(--text); flex: 1; }
  .wp-preview-coords { font-size: 11px; color: var(--text-faint); font-family: 'DM Mono', monospace; }
  .wp-dwell { font-size: 10px; color: var(--accent-warn); background: var(--accent-warn-bg); border-radius: 4px; padding: 1px 5px; flex-shrink: 0; }

  .trip-actions { display: flex; gap: 8px; flex-wrap: wrap; }
  .act {
    border: 1px solid; border-radius: 6px; padding: 4px 12px;
    font-family: inherit; font-size: 11px; font-weight: 600;
    cursor: pointer; background: transparent; transition: background 0.12s;
  }
  .act.start    { border-color: var(--accent);      color: var(--accent);      }
  .act.start:hover    { background: var(--accent-bg); }
  .act.complete { border-color: var(--accent-ok, #16a34a);    color: var(--accent-ok, #16a34a);    }
  .act.complete:hover { background: #f0fdf4; }
  .act.cancel-a { border-color: var(--accent-err);   color: var(--accent-err);  }
  .act.cancel-a:hover { background: var(--accent-err-bg); }
  .act.edit-act { border-color: var(--accent-warn);  color: var(--accent-warn); }
  .act.edit-act:hover { background: var(--accent-warn-bg); }
  .act.del      { border-color: var(--border);       color: var(--text-faint);  }
  .act.del:hover      { border-color: var(--accent-err); color: var(--accent-err); background: var(--accent-err-bg); }

  .empty { color: var(--text-faint); font-size: 13px; padding: 20px; text-align: center; }
</style>
