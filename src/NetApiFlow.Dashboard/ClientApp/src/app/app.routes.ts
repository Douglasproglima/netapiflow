import { Routes } from '@angular/router';
import { FlowPageComponent } from './features/flow/flow-page.component';
import { HistoryComponent } from './features/history/history.component';
import { ComponentsComponent } from './features/components/components.component';
import { RunnerComponent } from './features/runner/runner.component';

/**
 * One route per feature (seção 7.4 do prompt, D-23). `/flow` is the default screen and reads
 * `?trace=<traceId>` as a query string, never a route segment (seção 7.3 do prompt, tarefa 4.13).
 */
// `data.title`/`data.subtitle` are translation keys (core/i18n/translations.ts), not literal text:
// the topbar looks each one up in the active language's dictionary (ADR-0011).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'flow' },
  {
    path: 'flow',
    component: FlowPageComponent,
    data: { title: 'nav.flow', subtitle: 'topbar.subtitle.flow' },
  },
  {
    path: 'history',
    component: HistoryComponent,
    data: { title: 'nav.history', subtitle: 'topbar.subtitle.history' },
  },
  {
    path: 'components',
    component: ComponentsComponent,
    data: { title: 'nav.components', subtitle: 'topbar.subtitle.components' },
  },
  {
    path: 'runner',
    component: RunnerComponent,
    data: { title: 'nav.runner', subtitle: 'topbar.subtitle.runner' },
  },
];
