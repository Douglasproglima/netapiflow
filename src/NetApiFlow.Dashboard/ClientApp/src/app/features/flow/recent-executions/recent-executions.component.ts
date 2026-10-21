import { Component, inject, output, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../../core/api.service';
import { FlowTraceSummary } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';
import { httpMethodTagClass } from '../../../shared/http-method-style';

const PAGE_SIZE = 8;

/**
 * Condensed recent-executions list for the `/flow` workspace (D-31, tarefa 7.0c): the first page
 * of `GET /traces` (the same route `/history` already uses in full), so the last few requests are
 * visible without leaving the main screen. Clicking a row opens it the same way the live-in-progress
 * list already does (`openTrace`, reused by the parent).
 */
@Component({
  selector: 'app-recent-executions',
  templateUrl: './recent-executions.component.html',
  styleUrl: './recent-executions.component.scss',
  imports: [MatCardModule, MatButtonModule],
})
export class RecentExecutionsComponent {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly traces = signal<FlowTraceSummary[]>([]);
  readonly collapsed = signal(false);

  readonly traceSelected = output<string>();

  toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  constructor() {
    this.api.getTraces({ page: 1, pageSize: PAGE_SIZE }).subscribe({
      next: (result) => {
        this.traces.set(result.items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load recent executions.');
        this.loading.set(false);
      },
    });
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }
}
