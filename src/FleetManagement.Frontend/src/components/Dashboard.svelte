<script lang="ts">
  import { fleetSummary, vehiclesByStatus, vehicleList, lowFuelVehicles, telemetryMap } from '../stores/fleet';
  import { onMount, onDestroy } from 'svelte';
  import { Chart as ChartJS, registerables } from 'chart.js';

  ChartJS.register(...registerables);

  let fuelCanvas: HTMLCanvasElement;
  let speedCanvas: HTMLCanvasElement;
  let fuelChart: ChartJS | null = null;
  let speedChart: ChartJS | null = null;

  const statusColors: Record<string, string> = {
    Idle: '#2563eb', 'En Route': '#0ea882',
    Maintenance: '#d97706', Charging: '#7c3aed', 'Out Of Service': '#dc2626'
  };

  $: kpis = $fleetSummary ? [
    { label: 'Total Vehicles',  value: $fleetSummary.totalVehicles,                   icon: '▣', color: '#2563eb' },
    { label: 'En Route',        value: $fleetSummary.activeVehicles,                   icon: '◈', color: '#0ea882' },
    { label: 'Active Routes',   value: $fleetSummary.activeRoutes,                     icon: '◉', color: '#7c3aed' },
    { label: 'Avg Fuel',        value: `${$fleetSummary.avgFuelLevel.toFixed(1)}%`,    icon: '◎', color: '#d97706' },
  ] : [];

  // Build status donut data
  $: statusData = Object.entries($vehiclesByStatus).map(([k, vs]) => ({
    label: vs[0]?.status ?? k,
    count: vs.length,
    color: statusColors[vs[0]?.status ?? k] ?? '#aaa'
  }));

  // Fuel histogram bins
  $: {
    if (fuelChart && $vehicleList.length > 0) {
      const bins = [0,0,0,0,0]; // 0-20, 20-40, 40-60, 60-80, 80-100
      $vehicleList.forEach(v => bins[Math.min(4, Math.floor(v.fuelLevelPct / 20))]++);
      fuelChart.data.datasets[0].data = bins;
      fuelChart.update('none');
    }
  }

  // Speed distribution — prefer live telemetry, fall back to DB vehicle speeds
  $: speedSources = Object.keys($telemetryMap).length > 0
          ? Object.values($telemetryMap).map(t => t.speedKmh)
          : $vehicleList.map(v => v.speedKmh);
  $: {
    if (speedChart && speedSources.length > 0) {
      const bins = [0,0,0,0,0,0];
      speedSources.forEach(s => bins[Math.min(5, Math.floor(s / 20))]++);
      speedChart.data.datasets[0].data = bins;
      speedChart.update('none');
    }
  }

  function initCharts() {
    const base = {
      responsive: true, maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { ticks: { color: '#7a92a8', font: { family: 'Inter', size: 11 } }, grid: { color: '#e8eef5' } },
        y: { ticks: { color: '#7a92a8', font: { family: 'Inter', size: 11 } }, grid: { color: '#e8eef5' } }
      }
    };
    fuelChart = new ChartJS(fuelCanvas, {
      type: 'bar',
      data: {
        labels: ['0-20%','20-40%','40-60%','60-80%','80-100%'],
        datasets: [{
          data: [0,0,0,0,0],
          backgroundColor: ['#dc2626','#d97706','#ca8a04','#2563eb','#0ea882'],
          borderRadius: 5, borderSkipped: false
        }]
      },
      options: { ...base, plugins: { ...base.plugins, tooltip: { callbacks: { label: ctx => ` ${ctx.parsed.y} vehicles` } } } }
    });
    speedChart = new ChartJS(speedCanvas, {
      type: 'bar',
      data: {
        labels: ['0-20','20-40','40-60','60-80','80-100','100+'],
        datasets: [{
          data: [0,0,0,0,0,0],
          backgroundColor: '#0ea882',
          borderRadius: 5, borderSkipped: false
        }]
      },
      options: { ...base, plugins: { ...base.plugins, tooltip: { callbacks: { label: ctx => ` ${ctx.parsed.y} vehicles` } } } }
    });
  }

  onMount(initCharts);
  onDestroy(() => { fuelChart?.destroy(); speedChart?.destroy(); });
</script>

<div class="dashboard">
  <div class="page-header">
    <h1>Fleet Dashboard</h1>
    <p class="subtitle">
      {#if $fleetSummary}Last updated {new Date($fleetSummary.lastUpdated).toLocaleTimeString()}
      {:else}Loading…{/if}
    </p>
  </div>

  <div class="kpi-grid">
    {#each kpis as kpi}
      <div class="kpi-card" style="--kpi-color:{kpi.color}">
        <div class="kpi-top">
          <span class="kpi-icon">{kpi.icon}</span>
          <span class="kpi-label">{kpi.label}</span>
        </div>
        <div class="kpi-value">{kpi.value}</div>
      </div>
    {/each}
  </div>

  <div class="charts-row">
    <div class="card">
      <h3>Fuel Level Distribution</h3>
      <div class="chart-wrap"><canvas bind:this={fuelCanvas}></canvas></div>
    </div>
    <div class="card">
      <h3>Speed Distribution (km/h)</h3>
      <div class="chart-wrap"><canvas bind:this={speedCanvas}></canvas></div>
    </div>
  </div>

  <div class="bottom-row">
    <div class="card">
      <h3>Fleet Status</h3>
      <div class="status-list">
        {#each statusData as s}
          <div class="status-row">
            <span class="status-label">{s.label}</span>
            <div class="status-bar-track">
              <div class="status-bar-fill"
                style="width:{$fleetSummary && $fleetSummary.totalVehicles > 0 ? (s.count/$fleetSummary.totalVehicles*100).toFixed(1) : 0}%;background:{s.color}">
              </div>
            </div>
            <span class="status-count" style="color:{s.color}">{s.count}</span>
          </div>
        {/each}
      </div>
    </div>

    <div class="card">
      <h3>Low Fuel Vehicles <span class="warn-badge">{$lowFuelVehicles.length}</span></h3>
      <div class="fuel-list">
        {#each $lowFuelVehicles as v}
          <div class="fuel-row">
            <span class="plate">{v.licensePlate}</span>
            <div class="fuel-bar-track">
              <div class="fuel-bar-fill" style="width:{v.fuelLevelPct}%"></div>
            </div>
            <span class="fuel-pct" style="color:{v.fuelLevelPct < 10 ? '#dc2626' : '#d97706'}">{v.fuelLevelPct.toFixed(1)}%</span>
          </div>
        {:else}
          <p class="empty-state">✓ All vehicles have sufficient fuel</p>
        {/each}
      </div>
    </div>
  </div>
</div>

<style>
  .dashboard { display: flex; flex-direction: column; gap: 16px; padding: 24px; height: 100%; overflow-y: auto; }

  .page-header { display: flex; align-items: baseline; gap: 12px; }
  h1 { font-size: 20px; font-weight: 700; color: var(--text); }
  .subtitle { font-size: 12px; color: var(--text-faint); }

  .kpi-grid { display: grid; grid-template-columns: repeat(4,1fr); gap: 12px; }
  .kpi-card {
    background: var(--bg-panel);
    border: 1px solid var(--border);
    border-top: 3px solid var(--kpi-color, var(--accent));
    border-radius: 10px;
    padding: 16px 18px;
    box-shadow: var(--shadow-sm);
    transition: box-shadow 0.15s;
  }
  .kpi-card:hover { box-shadow: var(--shadow-md); }
  .kpi-top { display: flex; align-items: center; gap: 6px; margin-bottom: 8px; }
  .kpi-icon { font-size: 16px; color: var(--kpi-color, var(--accent)); }
  .kpi-label { font-size: 11px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .kpi-value { font-size: 30px; font-weight: 700; color: var(--text); line-height: 1; }

  .charts-row, .bottom-row { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
  .card { background: var(--bg-panel); border: 1px solid var(--border); border-radius: 10px; padding: 18px 20px; box-shadow: var(--shadow-sm); }
  h3 { font-size: 12px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.07em; margin-bottom: 14px; display: flex; align-items: center; gap: 8px; }
  .chart-wrap { height: 160px; position: relative; }

  .status-list { display: flex; flex-direction: column; gap: 10px; }
  .status-row { display: flex; align-items: center; gap: 8px; }
  .status-label { width: 90px; font-size: 12px; color: var(--text-muted); flex-shrink: 0; }
  .status-bar-track { flex: 1; height: 7px; background: var(--bg-hover); border-radius: 4px; overflow: hidden; }
  .status-bar-fill  { height: 100%; border-radius: 4px; transition: width 0.5s; }
  .status-count { font-size: 12px; font-weight: 700; width: 24px; text-align: right; flex-shrink: 0; color: var(--text-muted); }

  .warn-badge { background: var(--accent-warn-bg); color: var(--accent-warn); border-radius: 8px; padding: 1px 7px; font-size: 11px; font-weight: 700; text-transform: none; letter-spacing: 0; }
  .fuel-list  { display: flex; flex-direction: column; gap: 10px; }
  .fuel-row   { display: flex; align-items: center; gap: 8px; }
  .plate      { font-size: 12px; font-weight: 600; color: var(--text); width: 90px; flex-shrink: 0; font-family: 'DM Mono', monospace; }
  .fuel-bar-track { flex: 1; height: 7px; background: var(--bg-hover); border-radius: 4px; overflow: hidden; }
  .fuel-bar-fill  { height: 100%; border-radius: 4px; background: linear-gradient(90deg, #dc2626, #d97706); transition: width 0.5s; }
  .fuel-pct   { font-size: 12px; font-weight: 700; width: 42px; text-align: right; flex-shrink: 0; font-family: 'DM Mono', monospace; }
  .empty-state { font-size: 12px; color: var(--accent); padding: 8px 0; }
</style>
