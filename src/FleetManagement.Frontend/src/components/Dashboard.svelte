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
    Idle: '#3b82f6', 'En Route': '#22d3a5',
    Maintenance: '#f59e0b', Charging: '#a78bfa', 'Out Of Service': '#f43f5e'
  };

  $: kpis = $fleetSummary ? [
    { label: 'Total Vehicles',   value: $fleetSummary.totalVehicles,   icon: '▣',  color: '#60a5fa' },
    { label: 'Active En Route',  value: $fleetSummary.activeVehicles,  icon: '◈',  color: '#22d3a5' },
    { label: 'Active Routes',    value: $fleetSummary.activeRoutes,    icon: '◉',  color: '#a78bfa' },
    { label: 'Avg Fuel',         value: `${$fleetSummary.avgFuelLevel.toFixed(1)}%`, icon: '◎', color: '#f59e0b' },
  ] : [];

  // Build status donut data
  $: statusData = Object.entries($vehiclesByStatus).map(([k, vs]) => ({
    label: k, //=== 'En Route' ? 'En Route' : k.charAt(0).toUpperCase() + k.slice(1),
    count: vs.length,
    color: statusColors[vs[0]?.status ?? k] ?? '#555'
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

  // Speed distribution from telemetry
  $: telemetryEntries = Object.values($telemetryMap);
  $: {
    if (speedChart && telemetryEntries.length > 0) {
      const bins = [0,0,0,0,0,0]; // 0-20, 20-40, 40-60, 60-80, 80-100, 100+
      telemetryEntries.forEach(t => bins[Math.min(5, Math.floor(t.speedKmh / 20))]++);
      speedChart.data.datasets[0].data = bins;
      speedChart.update('none');
    }
  }

  function initCharts() {
    const chartDefaults = {
      responsive: true, maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { ticks: { color: '#4a637e', font: { family: 'DM Mono' } }, grid: { color: '#111d2c' } },
        y: { ticks: { color: '#4a637e', font: { family: 'DM Mono' } }, grid: { color: '#111d2c' } }
      }
    };

    fuelChart = new ChartJS(fuelCanvas, {
      type: 'bar',
      data: {
        labels: ['0-20%','20-40%','40-60%','60-80%','80-100%'],
        datasets: [{
          data: [0,0,0,0,0],
          backgroundColor: ['#f43f5e','#f59e0b','#facc15','#60a5fa','#22d3a5'],
          borderRadius: 4, borderSkipped: false
        }]
      },
      options: { ...chartDefaults,
        plugins: { ...chartDefaults.plugins,
          tooltip: { callbacks: { label: ctx => ` ${ctx.parsed.y} vehicles` } } }
      }
    });

    speedChart = new ChartJS(speedCanvas, {
      type: 'bar',
      data: {
        labels: ['0-20','20-40','40-60','60-80','80-100','100+'],
        datasets: [{
          data: [0,0,0,0,0,0],
          backgroundColor: '#22d3a5',
          borderRadius: 4, borderSkipped: false
        }]
      },
      options: { ...chartDefaults,
        plugins: { ...chartDefaults.plugins,
          tooltip: { callbacks: { label: ctx => ` ${ctx.parsed.y} vehicles` } } }
      }
    });
  }

  onMount(initCharts);
  onDestroy(() => { fuelChart?.destroy(); speedChart?.destroy(); });
</script>

<div class="dashboard">
  <!-- Header -->
  <div class="page-header">
    <div class="page-title">
      <span class="title-icon">⬡</span>
      <div>
        <h1>Fleet Dashboard</h1>
        <p class="subtitle">
          {#if $fleetSummary}
            Last updated {new Date($fleetSummary.lastUpdated).toLocaleTimeString()}
          {:else}
            Loading fleet data…
          {/if}
        </p>
      </div>
    </div>
  </div>

  <!-- KPI Cards -->
  <div class="kpi-grid">
    {#each kpis as kpi}
      <div class="kpi-card">
        <div class="kpi-icon" style="color:{kpi.color}">{kpi.icon}</div>
        <div class="kpi-value" style="color:{kpi.color}">{kpi.value}</div>
        <div class="kpi-label">{kpi.label}</div>
      </div>
    {/each}
  </div>

  <!-- Charts row -->
  <div class="charts-row">
    <div class="chart-card">
      <h3>Fuel Level Distribution</h3>
      <div class="chart-wrap"><canvas bind:this={fuelCanvas}></canvas></div>
    </div>
    <div class="chart-card">
      <h3>Speed Distribution (km/h)</h3>
      <div class="chart-wrap"><canvas bind:this={speedCanvas}></canvas></div>
    </div>
  </div>

  <!-- Bottom row -->
  <div class="bottom-row">
    <!-- Status breakdown -->
    <div class="status-card">
      <h3>Fleet Status</h3>
      <div class="status-list">
        {#each statusData as s}
          <div class="status-row">
            <div class="status-bar-wrap">
              <span class="status-label">{s.label}</span>
              <div class="status-bar-track">
                <div
                  class="status-bar-fill"
                  style="width:{$fleetSummary && $fleetSummary.totalVehicles > 0 ? (s.count/$fleetSummary.totalVehicles*100).toFixed(1) : 0}%; background:{s.color}"
                ></div>
              </div>
              <span class="status-count" style="color:{s.color}">{s.count}</span>
            </div>
          </div>
        {/each}
      </div>
    </div>

    <!-- Low fuel alerts -->
    <div class="status-card">
      <h3>Low Fuel Vehicles <span class="warn-count">{$lowFuelVehicles.length}</span></h3>
      <div class="fuel-list">
        {#each $lowFuelVehicles as v}
          <div class="fuel-row">
            <span class="plate">{v.licensePlate}</span>
            <div class="fuel-bar-track">
              <div class="fuel-bar-fill" style="width:{v.fuelLevelPct}%"></div>
            </div>
            <span class="fuel-pct" style="color:{v.fuelLevelPct < 10 ? '#f43f5e' : '#f59e0b'}">{v.fuelLevelPct.toFixed(1)}%</span>
          </div>
        {:else}
          <p class="empty-state">✓ All vehicles have sufficient fuel</p>
        {/each}
      </div>
    </div>
  </div>
</div>

<style>
  .dashboard {
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding: 24px;
    height: 100%;
    overflow-y: auto;
  }

  .page-header { display: flex; align-items: center; justify-content: space-between; }
  .page-title  { display: flex; align-items: center; gap: 14px; }
  .title-icon  { font-size: 32px; color: #22d3a5; line-height: 1; }
  h1 { font-size: 20px; font-weight: 700; color: #e2eaf4; letter-spacing: 0.04em; }
  .subtitle { font-size: 11px; color: #4a637e; margin-top: 2px; letter-spacing: 0.05em; }

  .kpi-grid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 12px;
  }
  .kpi-card {
    background: #0d1420;
    border: 1px solid #1e2d45;
    border-radius: 10px;
    padding: 20px 18px;
    display: flex;
    flex-direction: column;
    gap: 6px;
    transition: border-color 0.2s;
  }
  .kpi-card:hover { border-color: #2a4060; }
  .kpi-icon  { font-size: 22px; }
  .kpi-value { font-size: 32px; font-weight: 700; letter-spacing: -0.02em; line-height: 1; }
  .kpi-label { font-size: 11px; color: #4a637e; letter-spacing: 0.06em; text-transform: uppercase; }

  .charts-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }
  .chart-card {
    background: #0d1420;
    border: 1px solid #1e2d45;
    border-radius: 10px;
    padding: 18px 20px;
  }
  .chart-card h3 { font-size: 12px; color: #6b84a0; letter-spacing: 0.07em; text-transform: uppercase; margin-bottom: 14px; }
  .chart-wrap { height: 160px; position: relative; }

  .bottom-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }
  .status-card {
    background: #0d1420;
    border: 1px solid #1e2d45;
    border-radius: 10px;
    padding: 18px 20px;
  }
  .status-card h3 { font-size: 12px; color: #6b84a0; letter-spacing: 0.07em; text-transform: uppercase; margin-bottom: 14px; display: flex; align-items: center; gap: 8px; }
  .warn-count { background: #f59e0b22; color: #f59e0b; border-radius: 8px; padding: 1px 7px; font-size: 11px; }

  .status-list { display: flex; flex-direction: column; gap: 10px; }
  .status-row {}
  .status-bar-wrap { display: flex; align-items: center; gap: 8px; }
  .status-label { width: 90px; font-size: 11px; color: #6b84a0; flex-shrink: 0; }
  .status-bar-track { flex: 1; height: 6px; background: #111d2c; border-radius: 3px; overflow: hidden; }
  .status-bar-fill  { height: 100%; border-radius: 3px; transition: width 0.5s; }
  .status-count { font-size: 12px; font-weight: 700; width: 24px; text-align: right; flex-shrink: 0; }

  .fuel-list { display: flex; flex-direction: column; gap: 10px; }
  .fuel-row  { display: flex; align-items: center; gap: 8px; }
  .plate     { font-size: 11px; color: #c8d6e8; width: 90px; flex-shrink: 0; font-weight: 600; }
  .fuel-bar-track { flex: 1; height: 6px; background: #111d2c; border-radius: 3px; overflow: hidden; }
  .fuel-bar-fill  { height: 100%; border-radius: 3px; background: linear-gradient(90deg, #f43f5e, #f59e0b); transition: width 0.5s; }
  .fuel-pct  { font-size: 11px; font-weight: 700; width: 40px; text-align: right; flex-shrink: 0; }

  .empty-state { font-size: 12px; color: #22d3a5; padding: 12px 0; }
</style>
