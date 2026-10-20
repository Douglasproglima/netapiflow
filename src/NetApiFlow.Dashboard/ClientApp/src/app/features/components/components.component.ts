import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ComponentStat } from '../../core/models';
import { formatDurationMicros } from '../../shared/duration-format';

/** Per component/operation metrics, from GET /metrics/components. */
@Component({
  selector: 'app-components',
  templateUrl: './components.component.html',
  styleUrl: './components.component.scss',
})
export class ComponentsComponent {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly stats = signal<ComponentStat[]>([]);

  constructor() {
    this.api.getComponentMetrics({}).subscribe({
      next: (stats) => {
        this.stats.set(stats);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load component metrics.');
        this.loading.set(false);
      },
    });
  }

  formatDuration(micros: number): string {
    return formatDurationMicros(micros);
  }
}
