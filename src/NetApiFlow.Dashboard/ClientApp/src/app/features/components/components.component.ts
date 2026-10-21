import { Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { ApiService } from '../../core/api.service';
import { ComponentStat } from '../../core/models';
import { formatDurationMicros } from '../../shared/duration-format';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationKey } from '../../core/i18n/translations';

/** Per component/operation metrics, from GET /metrics/components. */
@Component({
  selector: 'app-components',
  templateUrl: './components.component.html',
  styleUrl: './components.component.scss',
  imports: [MatCardModule, TranslatePipe],
})
export class ComponentsComponent {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);
  readonly stats = signal<ComponentStat[]>([]);

  constructor() {
    this.api.getComponentMetrics({}).subscribe({
      next: (stats) => {
        this.stats.set(stats);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('components.error');
        this.loading.set(false);
      },
    });
  }

  formatDuration(micros: number): string {
    return formatDurationMicros(micros);
  }
}
