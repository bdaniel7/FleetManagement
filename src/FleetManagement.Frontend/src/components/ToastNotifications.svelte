<script lang="ts">
  import {alerts, mapFocusVehicleId} from '../stores/fleet';
  import { push } from 'svelte-spa-router';
  import {type AlertMessage, splitMessage} from "$lib/common.ts";

  interface ToastAlert {
    id: string;
    message: string;
    priority: string;
    vehicleId?: string;
    timestamp: string;
    level?: string;
  }

  let toasts: ToastAlert[] = [];
  let dismissed = new Set<string>();

  $: {
    // Keep toasts that are already showing
    toasts = $alerts
            .filter((a: ToastAlert) => !dismissed.has(a.id))
            .slice(0, 5);
  }

  function dismiss(id: string) {
    dismissed.add(id);
    dismissed = dismissed;
    toasts = toasts.filter(t => t.id !== id);
  }

  function focusVehicle(vehicleId: string | undefined) {
    if (vehicleId) {
      mapFocusVehicleId.set(vehicleId);
      push('/live-map');
    }
  }

  let alert : AlertMessage;

  function getIcon(priority: string): string {
    switch (priority) {
      case 'Emergency': return '🚨';
      case 'High': return '⚠️';
      case 'Normal': return 'ℹ️';
      case 'Low': return '💡';
      default: return '📢';
    }
  }

  function getClass(priority: string): string {
    switch (priority) {
      case 'Emergency': return 'toast-critical';
      case 'High': return 'toast-warning';
      case 'Normal': return 'toast-info';
      default: return 'toast-default';
    }
  }
</script>

<div class="toast-container">
  {#each toasts as toast (toast.id)}
    <div class="toast {getClass(toast.priority)}">
      <span class="toast-icon">{getIcon(toast.priority)}</span>
      <div class="toast-content">
        <span class="toast-message">
          {#if toast.vehicleId}
            {@const alert = splitMessage(toast.message)}
            {alert.start}
            <button class="plate-link" on:click={() => focusVehicle(toast.vehicleId)}>
              {alert.licensePlate}
            </button>
            {alert.end}
          {:else}
            {toast.message}
          {/if}
        </span>
        <span class="toast-time">{new Date(toast.timestamp).toLocaleTimeString()}</span>
      </div>
      <button class="toast-close" on:click={() => dismiss(toast.id)}>×</button>
    </div>
  {/each}
</div>

<style>
  .toast-container {
    position: fixed;
    top: 16px;
    right: 16px;
    z-index: 9999;
    display: flex;
    flex-direction: column;
    gap: 8px;
    max-width: 380px;
    pointer-events: none;
  }

  .toast {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    padding: 12px 14px;
    border-radius: 8px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.15);
    pointer-events: auto;
    animation: slideIn 0.2s ease-out;
    border-left: 4px solid;
  }

  @keyframes slideIn {
    from {
      opacity: 0;
      transform: translateX(100%);
    }
    to {
      opacity: 1;
      transform: translateX(0);
    }
  }

  .toast-critical {
    background: #fef2f2;
    border-color: #dc2626;
  }

  .toast-warning {
    background: #fffbeb;
    border-color: #f59e0b;
  }

  .toast-info {
    background: #eff6ff;
    border-color: #3b82f6;
  }

  .toast-default {
    background: var(--sidebar-bg, #f8fafc);
    border-color: #94a3b8;
  }

  .toast-icon {
    font-size: 18px;
    flex-shrink: 0;
    line-height: 1.3;
  }

  .toast-content {
    flex: 1;
    min-width: 0;
  }

  .toast-message {
    display: block;
    font-size: 13px;
    font-weight: 500;
    color: #1e293b;
    line-height: 1.4;
  }

  .toast-time {
    display: block;
    font-size: 11px;
    color: #64748b;
    margin-top: 4px;
    font-family: 'DM Mono', monospace;
  }

  .toast-close {
    background: none;
    border: none;
    font-size: 18px;
    color: #94a3b8;
    cursor: pointer;
    padding: 0 2px;
    line-height: 1;
    flex-shrink: 0;
  }

  .toast-close:hover {
    color: #475569;
  }

  .plate-link {
    background: none;
    border: none;
    padding: 0;
    font: inherit;
    color: var(--accent, #2563eb);
    cursor: pointer;
    text-decoration: underline;
    font-weight: 600;
  }

  .plate-link:hover {
    color: #1d4ed8;
  }

  @media (max-width: 480px) {
    .toast-container {
      top: 8px;
      right: 8px;
      left: 8px;
      max-width: none;
    }
  }
</style>
