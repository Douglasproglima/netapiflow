import { Routes } from '@angular/router';
import { FlowPageComponent } from './features/flow/flow-page.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'flow' },
  {
    path: 'flow',
    component: FlowPageComponent,
    data: { title: 'nav.flow', subtitle: 'topbar.subtitle.flow' },
  },
  {
    path: 'history',
    loadComponent: () =>
      import('./features/history/history.component').then((m) => m.HistoryComponent),
    data: { title: 'nav.history', subtitle: 'topbar.subtitle.history' },
  },
  {
    path: 'components',
    loadComponent: () =>
      import('./features/components/components.component').then((m) => m.ComponentsComponent),
    data: { title: 'nav.components', subtitle: 'topbar.subtitle.components' },
  },
  {
    path: 'insights',
    loadComponent: () =>
      import('./features/insights/insights.component').then((m) => m.InsightsComponent),
    data: { title: 'nav.insights', subtitle: 'topbar.subtitle.insights' },
  },
  {
    path: 'runner',
    loadComponent: () =>
      import('./features/runner/runner.component').then((m) => m.RunnerComponent),
    data: { title: 'nav.runner', subtitle: 'topbar.subtitle.runner' },
  },
];
