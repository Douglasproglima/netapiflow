import { Routes } from '@angular/router';
import { FlowPageComponent } from './features/flow/flow-page.component';
import { HistoryComponent } from './features/history/history.component';
import { ComponentsComponent } from './features/components/components.component';
import { RunnerComponent } from './features/runner/runner.component';

/**
 * One route per feature (seção 7.4 do prompt, D-23). `/flow` is the default screen and reads
 * `?trace=<traceId>` as a query string, never a route segment (seção 7.3 do prompt, tarefa 4.13).
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'flow' },
  { path: 'flow', component: FlowPageComponent },
  { path: 'history', component: HistoryComponent },
  { path: 'components', component: ComponentsComponent },
  { path: 'runner', component: RunnerComponent },
];
