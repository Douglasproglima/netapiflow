import { Component, effect, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../core/api.service';
import { FlowStatus, PagedResult, FlowTraceSummary, TraceQuery } from '../../core/models';
import { formatDurationMicros } from '../../shared/duration-format';
import { httpMethodTagClass } from '../../shared/http-method-style';

const DEFAULT_PAGE_SIZE = 50;

function inputValue(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}

/** Paginated trace history with the filters of seção 7.1, item 3 do prompt. */
@Component({
  selector: 'app-history',
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss',
  imports: [MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
})
export class HistoryComponent {
  private readonly api = inject(ApiService);

  readonly filters = signal<TraceQuery>({ page: 1, pageSize: DEFAULT_PAGE_SIZE });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<PagedResult<FlowTraceSummary> | null>(null);

  constructor() {
    effect(() => {
      const query = this.filters();
      this.loading.set(true);
      this.error.set(null);

      this.api.getTraces(query).subscribe({
        next: (result) => {
          this.result.set(result);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Failed to load traces.');
          this.loading.set(false);
        },
      });
    });
  }

  onStatusChange(event: Event): void {
    const value = inputValue(event) as FlowStatus | '';
    this.applyFilter({ status: value === '' ? undefined : value });
  }

  onMethodChange(event: Event): void {
    this.applyFilter({ method: inputValue(event) || undefined });
  }

  onRouteChange(event: Event): void {
    this.applyFilter({ route: inputValue(event) || undefined });
  }

  onTenantChange(event: Event): void {
    this.applyFilter({ tenant: inputValue(event) || undefined });
  }

  onFromChange(event: Event): void {
    this.applyFilter({ from: inputValue(event) || undefined });
  }

  onToChange(event: Event): void {
    this.applyFilter({ to: inputValue(event) || undefined });
  }

  onMinDurationChange(event: Event): void {
    const value = inputValue(event);
    this.applyFilter({ minDurationMicros: value === '' ? undefined : Number(value) });
  }

  setPage(page: number): void {
    this.filters.update((current) => ({ ...current, page }));
  }

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  private applyFilter(patch: Partial<TraceQuery>): void {
    this.filters.update((current) => ({ ...current, ...patch, page: 1 }));
  }
}
