<script lang="ts">
  import { alerts } from '../stores/fleet';

  const priorityColor: Record<string, string> = {
    Emergency: '#f43f5e',
    High:      '#f59e0b',
    Normal:    '#60a5fa',
    Low:       '#4a637e',
  };

  const priorityBg: Record<string, string> = {
    Emergency: '#2d0f18',
    High:      '#2d1f0a',
    Normal:    '#0f1d35',
    Low:       '#111d2c',
  };

  function clearAll() { alerts.set([]); }
  function dismiss(id: string) { alerts.update(as => as.filter(a => a.id !== id)); }
</script>

<div class="alerts-page">
  <div class="page-header">
    <span class="title-icon">◉</span>
    <h1>Fleet Alerts</h1>
    <span class="count">{$alerts.length} active</span>
    {#if $alerts.length > 0}
      <button class="clear-btn" on:click={clearAll}>Clear All</button>
    {/if}
  </div>

  <div class="alert-list">
    {#each $alerts as alert (alert.id)}
      <div class="alert-card" style="background:{priorityBg[alert.priority]};border-color:{priorityColor[alert.priority]}33">
        <div class="alert-left">
          <span class="priority-pip" style="background:{priorityColor[alert.priority]}"></span>
          <div class="alert-body">
            <div class="alert-priority" style="color:{priorityColor[alert.priority]}">{alert.priority}</div>
            <div class="alert-message">{alert.message}</div>
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
  .alerts-page { display: flex; flex-direction: column; height: 100%; overflow: hidden; }
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
  .count { font-size: 11px; color: #4a637e; margin-right: auto; }
  .clear-btn {
    background: transparent;
    border: 1px solid #1e2d45;
    color: #6b84a0;
    border-radius: 6px;
    padding: 5px 12px;
    font-family: inherit;
    font-size: 11px;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .clear-btn:hover { border-color: #f43f5e; color: #f43f5e; }

  .alert-list {
    flex: 1;
    overflow-y: auto;
    padding: 16px 24px;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .alert-card {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    border-radius: 10px;
    border: 1px solid;
    padding: 14px 16px;
    transition: opacity 0.2s;
  }
  .alert-left { display: flex; align-items: flex-start; gap: 12px; }
  .priority-pip {
    width: 3px;
    height: 48px;
    border-radius: 2px;
    flex-shrink: 0;
    margin-top: 2px;
  }
  .alert-body { display: flex; flex-direction: column; gap: 3px; }
  .alert-priority { font-size: 10px; font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase; }
  .alert-message  { font-size: 13px; color: #e2eaf4; line-height: 1.4; }
  .alert-time     { font-size: 11px; color: #4a637e; }
  .dismiss-btn {
    background: transparent;
    border: none;
    color: #4a637e;
    cursor: pointer;
    font-size: 13px;
    padding: 2px 6px;
    border-radius: 4px;
    flex-shrink: 0;
    transition: color 0.15s;
  }
  .dismiss-btn:hover { color: #f43f5e; }

  .no-alerts {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    color: #4a637e;
  }
  .no-alerts-icon { font-size: 48px; color: #22d3a5; }
  .no-alerts p { font-size: 14px; color: #6b84a0; }
  .no-alerts-sub { font-size: 12px; color: #4a637e; }
</style>
