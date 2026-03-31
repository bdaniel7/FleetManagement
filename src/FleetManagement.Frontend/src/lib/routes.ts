// src/lib/routes.ts — central route table
import Dashboard    from '../components/Dashboard.svelte';
import FleetMap     from '../components/FleetMap.svelte';
import VehicleTable from '../components/VehicleTable.svelte';
import RoutePlanner from '../components/RoutePlanner.svelte';
import TripPlanner  from '../components/TripPlanner.svelte';
import AlertPanel   from '../components/AlertPanel.svelte';

// svelte-spa-router uses hash routing by default: /#/dashboard
// We keep hash routing — no server config needed, works in Docker/Nginx as-is.
// The nav links below are plain strings used by push() / link action.

export interface NavItem {
  path:  string;
  icon:  string;
  label: string;
}

export const navItems: NavItem[] = [
  { path: '/dashboard', icon: '⬡', label: 'Dashboard' },
  { path: '/live-map',  icon: '◎', label: 'Live Map'  },
  { path: '/vehicles',  icon: '▣', label: 'Vehicles'  },
  { path: '/routes',    icon: '◈', label: 'Routes'    },
  { path: '/trips',     icon: '⟳', label: 'Trips'     },
  { path: '/alerts',    icon: '◉', label: 'Alerts'    },
];

// Route map consumed by svelte-spa-router <Router>
const routes: Record<string, any> = {
  '/':           Dashboard,
  '/dashboard':  Dashboard,
  '/live-map':   FleetMap,
  '/vehicles':   VehicleTable,
  '/routes':     RoutePlanner,
  '/trips':      TripPlanner,
  '/alerts':     AlertPanel,
};

export default routes;
