<script lang="ts">
  import { onMount } from 'svelte';
  import { alerts, mapFocusVehicleId, loadAlerts } from '../stores/fleet';
  import { push } from 'svelte-spa-router';
  import {type AlertMessage, splitMessage} from "$lib/common.ts";

  const priorityColor: Record<string, string> = {
    Emergency: '#dc2626', High: '#d97706', Normal: '#2563eb', Low: '#7a92a8',
  };
  const priorityBg: Record<string, string> = {
    Emergency: '#fee2e2', High: '#fef3c7', Normal: '#eff6ff', Low: '#f0f4f8',
  };
  const priorityBorder: Record<string, string> = {
    Emergency: '#fca5a5', High: '#fcd34d', Normal: '#93c5fd', Low: '#d0dae6',
  };

  function clearAll() { alerts.set([]); }
  function dismiss(id: string) { alerts.update(as => as.filter(a => a.id !== id)); }

  function focusVehicle(vehicleId: string | undefined) {
    if (vehicleId) {
      mapFocusVehicleId.set(vehicleId);
      push('/live-map');
    }
  }

  onMount(() => {
    loadAlerts();
  });

</script>

<div class="alerts-page">
  <div class="page-header">
    <div class="header-left">
      <h1>Fleet Alerts</h1>
      <span class="count-badge">{$alerts.length} active</span>
    </div>
    {#if $alerts.length > 0}
      <button class="clear-btn" on:click={clearAll}>Clear All</button>
    {/if}
  </div>

  <div class="alert-list">
    {#each $alerts as alert (alert.id)}
      <div class="alert-card"
        style="background:{priorityBg[alert.priority]};border-color:{priorityBorder[alert.priority]}">
        <div class="alert-left">
          <span class="priority-bar" style="background:{priorityColor[alert.priority]}"></span>
          <div class="alert-body">
            <div class="alert-priority" style="color:{priorityColor[alert.priority]}">{alert.priority}</div>
            <div class="alert-message">
              {#if alert.vehicleId}
                {@const alerts = splitMessage(alert.message)}
                {alerts.start}
                <button class="plate-link" on:click={() => focusVehicle(alert.vehicleId)}>
                  {alerts.licensePlate}
                </button>
                {alerts.end}
              {:else}
                {alert.message}
              {/if}
            </div>
            <div class="alert-time">{new Date(alert.timestamp).toLocaleString()}</div>
          </div>
        </div>
        <button class="dismiss-btn" on:click={() => dismiss(alert.id)}>✕</button>
      </div>
    {:else}
      <div class="no-alerts">
        <span class="no-alerts-icon">✓</span>
        <p>No active alerts</p>
        <p class="no-alerts-sub">Fleet is operating normally</p>
      </div>
    {/each}
  </div>
</div>

<style>
  .alerts-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; background: var(--bg); }
  .page-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 24px;
    background: var(--bg-panel);
    border-bottom: 1px solid var(--border);
    box-shadow: var(--shadow-sm);
    flex-shrink: 0;
  }
  .header-left { display: flex; align-items: center; gap: 10px; }
  h1 { font-size: 17px; font-weight: 700; color: var(--text); }
  .count-badge { background: var(--bg-hover); color: var(--text-faint); border-radius: 8px; padding: 2px 10px; font-size: 11px; font-weight: 600; }
  .clear-btn {
    background: transparent; border: 1px solid var(--border); color: var(--text-muted);
    border-radius: 7px; padding: 5px 14px; font-family: inherit; font-size: 12px; cursor: pointer;
    transition: border-color 0.12s, color 0.12s, background 0.12s;
  }
  .clear-btn:hover { border-color: var(--accent-err); color: var(--accent-err); background: var(--accent-err-bg); }

  .alert-list {
    flex: 1; overflow-y: auto; padding: 16px 24px;
    display: flex; flex-direction: column; gap: 10px;
  }

  .alert-card {
    display: flex; align-items: flex-start; justify-content: space-between;
    border: 1px solid; border-radius: 10px; padding: 14px 16px;
    box-shadow: var(--shadow-sm);
    transition: box-shadow 0.15s;
  }
  .alert-card:hover { box-shadow: var(--shadow-md); }
  .alert-left { display: flex; align-items: flex-start; gap: 12px; }
  .priority-bar { width: 4px; height: 52px; border-radius: 2px; flex-shrink: 0; margin-top: 2px; }
  .alert-body { display: flex; flex-direction: column; gap: 3px; }
  .alert-priority { font-size: 10px; font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase; }
  .alert-message  { font-size: 13px; color: var(--text); font-weight: 500; line-height: 1.4; }
  .alert-time     { font-size: 11px; color: var(--text-faint); font-family: 'DM Mono', monospace; }

  .plate-link {
    background: none; border: none; padding: 0; font: inherit;
    color: var(--accent, #2563eb); cursor: pointer; text-decoration: underline; font-weight: 600;
  }
  .plate-link:hover { color: #1d4ed8; }

  .dismiss-btn {
    background: transparent; border: none; color: var(--text-faint);
    cursor: pointer; font-size: 13px; padding: 2px 6px; border-radius: 4px;
    flex-shrink: 0; transition: color 0.12s, background 0.12s;
  }
  .dismiss-btn:hover { color: var(--accent-err); background: var(--accent-err-bg); }

  .no-alerts {
    flex: 1; display: flex; flex-direction: column;
    align-items: center; justify-content: center; gap: 8px; color: var(--text-faint);
  }
  .no-alerts-icon { font-size: 48px; color: var(--accent); }
  .no-alerts p    { font-size: 14px; color: var(--text-muted); font-weight: 500; }
  .no-alerts-sub  { font-size: 12px; color: var(--text-faint); }
</style>
