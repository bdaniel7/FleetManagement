<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import Router, { link, location } from 'svelte-spa-router';
  import { loadAll, connectHub, disconnectHub, hubStatus, fleetSummary, alerts, error } from './stores/fleet';
  import routes, { navItems } from '$lib/routes';

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
    connected:    'var(--accent)',
    connecting:   'var(--accent-warn)',
    reconnecting: 'var(--accent-warn)',
    disconnected: 'var(--accent-err)',
  };

  $: hubColor     = hubColors[$hubStatus] ?? 'var(--text-faint)';
  $: unreadAlerts  = $alerts.filter(a => a.priority === 'Emergency' || a.priority === 'High').length;
  $: activePath    = $location;
</script>

<div class="shell">
  <!-- Sidebar -->
  <nav class="sidebar">
    <div class="brand">
      <span class="brand-icon">⬡</span>
      <span class="brand-text">FLiT<em>OS</em></span>
    </div>

    <div class="nav-items">
      {#each navItems as item}
        <a
          href={item.path}
          use:link
          class="nav-btn"
          class:active={activePath === item.path || (item.path === '/dashboard' && (activePath === '/' || activePath === ''))}
        >
          <span class="nav-icon">{item.icon}</span>
          <span class="nav-label">{item.label}</span>
          {#if item.path === '/alerts' && unreadAlerts > 0}
            <span class="badge">{unreadAlerts}</span>
          {/if}
        </a>
      {/each}
    </div>

    <div class="sidebar-footer">
      <div class="hub-status">
        <span class="hub-dot" style="background:{hubColor}"></span>
        <span class="hub-label">{$hubStatus}</span>
      </div>
      {#if $fleetSummary}
        <div class="sidebar-stat"><span>{$fleetSummary.totalVehicles}</span> vehicles</div>
        <div class="sidebar-stat"><span>{$fleetSummary.activeRoutes}</span> active routes</div>
      {/if}
    </div>
  </nav>

  <!-- Main -->
  <main class="main">
    {#if $error}
      <div class="error-banner">⚠ {$error}</div>
    {/if}

    <Router {routes} />
  </main>
</div>

<style>
  :global(*) { box-sizing: border-box; margin: 0; padding: 0; }

  .shell {
    display: grid;
    grid-template-columns: 220px 1fr;
    height: 100dvh;
    overflow: hidden;
  }

  /* ── Sidebar ── */
  .sidebar {
    background: var(--sidebar-bg);
    border-right: 1px solid var(--sidebar-border);
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 18px 16px;
    border-bottom: 1px solid var(--border);
  }
  .brand-icon { font-size: 26px; color: var(--accent); line-height: 1; }
  .brand-text {
    font-family: 'DM Mono', monospace;
    font-size: 14px;
    font-weight: 600;
    letter-spacing: 0.1em;
    color: var(--text);
    text-transform: uppercase;
  }
  .brand-text em {
    font-style: normal;
    color: var(--accent);
  }

  .nav-items {
    flex: 1;
    padding: 12px 8px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .nav-btn {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 9px 12px;
    background: transparent;
    border: 1px solid transparent;
    border-radius: 8px;
    color: var(--sidebar-text);
    cursor: pointer;
    font-family: inherit;
    font-size: 13px;
    font-weight: 500;
    text-align: left;
    text-decoration: none;
    transition: background 0.12s, color 0.12s;
    position: relative;
  }
  .nav-btn:hover { background: var(--bg-hover); color: var(--text); }
  .nav-btn.active {
    background: var(--sidebar-active-bg);
    color: var(--sidebar-active-text);
    border-color: var(--accent);
    font-weight: 600;
  }
  .nav-icon  { font-size: 15px; flex-shrink: 0; opacity: 0.8; }
  .nav-label { flex: 1; }
  .badge {
    background: var(--accent-err);
    color: #fff;
    font-size: 10px;
    font-weight: 700;
    border-radius: 10px;
    padding: 1px 6px;
    min-width: 18px;
    text-align: center;
  }

  .sidebar-footer {
    padding: 12px 14px;
    border-top: 1px solid var(--border);
    display: flex;
    flex-direction: column;
    gap: 5px;
  }
  .hub-status { display: flex; align-items: center; gap: 7px; margin-bottom: 2px; }
  .hub-dot {
    width: 8px; height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
    transition: background 0.3s;
  }
  .hub-label { font-size: 11px; color: var(--text-faint); text-transform: capitalize; font-family: 'DM Mono', monospace; }
  .sidebar-stat { font-size: 11px; color: var(--text-muted); }
  .sidebar-stat span { color: var(--accent); font-weight: 700; }

  /* ── Main ── */
  .main {
    overflow: hidden;
    display: flex;
    flex-direction: column;
    background: var(--bg);
  }
  :global(.main > div) { height: 100%; }

  .error-banner {
    background: var(--accent-err-bg);
    color: var(--accent-err);
    border-bottom: 1px solid #fca5a5;
    padding: 8px 20px;
    font-size: 12px;
    flex-shrink: 0;
  }
</style>
