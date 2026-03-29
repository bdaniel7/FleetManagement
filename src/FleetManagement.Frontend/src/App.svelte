<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { loadAll, connectHub, disconnectHub, hubStatus, fleetSummary, alerts, error } from './stores/fleet';
  import Dashboard from './components/Dashboard.svelte';
  import FleetMap from './components/FleetMap.svelte';
  import VehicleTable from './components/VehicleTable.svelte';
  import RoutePlanner from './components/RoutePlanner.svelte';
  import AlertPanel from './components/AlertPanel.svelte';

  type View = 'dashboard' | 'map' | 'vehicles' | 'routes' | 'alerts';
  let activeView: View = 'dashboard';

  const navItems: { id: View; icon: string; label: string }[] = [
    { id: 'dashboard', icon: '⬡',  label: 'Dashboard'  },
    { id: 'map',       icon: '◎',  label: 'Live Map'    },
    { id: 'vehicles',  icon: '▣',  label: 'Vehicles'    },
    { id: 'routes',    icon: '◈',  label: 'Routes'      },
    { id: 'alerts',    icon: '◉',  label: 'Alerts'      },
  ];

  onMount(() => {
    void (async() => {
      await connectHub();
      await loadAll();
      // Refresh summary every 30s
      const timer = setInterval(loadAll, 30_000);
      return () => clearInterval(timer);
    })();
  });

  onDestroy(disconnectHub);

  const hubColors: Record<string, string> = {
    connected:    '#22d3a5',
    connecting:   '#f59e0b',
    reconnecting: '#f59e0b',
    disconnected: '#f43f5e',
  };

  $: hubColor = hubColors[$hubStatus] ?? '#888';

  $: unreadAlerts = $alerts.filter(a => a.priority === 'Emergency' || a.priority === 'High').length;
</script>

<div class="shell">
  <!-- Sidebar -->
  <nav class="sidebar">
    <div class="brand">
      <span class="brand-icon">⬡</span>
      <span class="brand-text">FLEET<br><em>OS</em></span>
    </div>

    <div class="nav-items">
      {#each navItems as item}
        <button
          class="nav-btn"
          class:active={activeView === item.id}
          on:click={() => activeView = item.id}
        >
          <span class="nav-icon">{item.icon}</span>
          <span class="nav-label">{item.label}</span>
          {#if item.id === 'alerts' && unreadAlerts > 0}
            <span class="badge">{unreadAlerts}</span>
          {/if}
        </button>
      {/each}
    </div>

    <div class="sidebar-footer">
      <div class="hub-status">
        <span class="hub-dot" style="background:{hubColor}"></span>
        <span class="hub-label">{$hubStatus}</span>
      </div>
      {#if $fleetSummary}
        <div class="sidebar-stat">
          <span>{$fleetSummary.totalVehicles}</span> vehicles
        </div>
        <div class="sidebar-stat">
          <span>{$fleetSummary.activeRoutes}</span> active routes
        </div>
      {/if}
    </div>
  </nav>

  <!-- Main -->
  <main class="main">
    {#if $error}
      <div class="error-banner">⚠ {$error}</div>
    {/if}

    {#if activeView === 'dashboard'}
      <Dashboard />
    {:else if activeView === 'map'}
      <FleetMap />
    {:else if activeView === 'vehicles'}
      <VehicleTable />
    {:else if activeView === 'routes'}
      <RoutePlanner />
    {:else if activeView === 'alerts'}
      <AlertPanel />
    {/if}
  </main>
</div>

<style>
  :global(*) { box-sizing: border-box; margin: 0; padding: 0; }
  :global(body) {
    font-family: 'DM Mono', 'Fira Code', monospace;
    background: #e7ec4e;
    color: #134b93;
    height: 100dvh;
    overflow: hidden;
  }

  .shell {
    display: grid;
    grid-template-columns: 220px 1fr;
    height: 100dvh;
    overflow: hidden;
  }

  /* ── Sidebar ── */
  .sidebar {
    background: #0d1420;
    border-right: 1px solid #1e2d45;
    display: flex;
    flex-direction: column;
    padding: 0;
    overflow: hidden;
  }

  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 20px 18px;
    border-bottom: 1px solid #1e2d45;
  }
  .brand-icon {
    font-size: 28px;
    color: #22d3a5;
    line-height: 1;
  }
  .brand-text {
    font-size: 13px;
    font-weight: 700;
    letter-spacing: 0.12em;
    color: #e2eaf4;
    line-height: 1.3;
    text-transform: uppercase;
  }
  .brand-text em {
    font-style: normal;
    color: #22d3a5;
    font-size: 11px;
    letter-spacing: 0.2em;
  }

  .nav-items {
    flex: 1;
    padding: 16px 10px;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .nav-btn {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 10px 12px;
    background: transparent;
    border: none;
    border-radius: 8px;
    color: #6b84a0;
    cursor: pointer;
    font-family: inherit;
    font-size: 12.5px;
    letter-spacing: 0.05em;
    text-align: left;
    transition: background 0.15s, color 0.15s;
    position: relative;
  }
  .nav-btn:hover { background: #141e2e; color: #a8bdd4; }
  .nav-btn.active {
    background: #0f2040;
    color: #22d3a5;
    border: 1px solid #1a3356;
  }
  .nav-icon { font-size: 16px; flex-shrink: 0; }
  .nav-label { flex: 1; }
  .badge {
    background: #f43f5e;
    color: #fff;
    font-size: 10px;
    font-weight: 700;
    border-radius: 10px;
    padding: 1px 6px;
    min-width: 18px;
    text-align: center;
  }

  .sidebar-footer {
    padding: 14px 16px;
    border-top: 1px solid #1e2d45;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .hub-status {
    display: flex;
    align-items: center;
    gap: 7px;
    margin-bottom: 4px;
  }
  .hub-dot {
    width: 8px; height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
    box-shadow: 0 0 6px currentColor;
    transition: background 0.3s;
  }
  .hub-label { font-size: 11px; color: #6b84a0; text-transform: capitalize; }
  .sidebar-stat { font-size: 11px; color: #4a637e; }
  .sidebar-stat span { color: #22d3a5; font-weight: 700; }

  /* ── Main ── */
  .main {
    overflow: hidden;
    display: flex;
    flex-direction: column;
    background: #080c12;
  }

  .error-banner {
    background: #2d0f18;
    color: #f43f5e;
    border-bottom: 1px solid #4d1a28;
    padding: 8px 20px;
    font-size: 12px;
    letter-spacing: 0.04em;
  }
</style>
