import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Params, ParamMap, Router, RouterLink } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { ApiService } from '../../core/api.service';
import {
  FlowStatus,
  NodeVisualState,
  PagedResult,
  FlowTraceSummary,
  TraceQuery,
} from '../../core/models';
import { formatDurationMicros } from '../../shared/duration-format';
import { httpMethodTagClass } from '../../shared/http-method-style';
import { NODE_STATE_PRESENTATION } from '../../shared/node-state-presentation';
import { periodPresetFrom, PeriodPreset } from '../../shared/period-preset';
import { formatStartedAtFull, formatStartedAtShort } from '../../shared/started-at-format';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationKey } from '../../core/i18n/translations';

const DEFAULT_PAGE_SIZE = 50;
const PAGE_SIZES: readonly number[] = [10, 20, 50, 100];
const METHODS: readonly string[] = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
const PERIOD_PRESETS: readonly PeriodPreset[] = ['15m', '1h', '24h', '7d'];

type ViewState = 'loading' | 'error' | 'empty-none' | 'empty-filtered' | 'data';

function inputValue(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}

function isFlowStatus(value: string | null): value is FlowStatus {
  return value === 'Running' || value === 'Success' || value === 'Error';
}

function queryFromParamMap(params: ParamMap): TraceQuery {
  const status = params.get('status');
  const method = params.get('method');
  const route = params.get('route');
  const tenant = params.get('tenant');
  const from = params.get('from');
  const to = params.get('to');
  const minDurationMicros = params.get('minDurationMicros');
  const page = params.get('page');
  const pageSize = params.get('pageSize');

  return {
    status: isFlowStatus(status) ? status : undefined,
    method: method ?? undefined,
    route: route ?? undefined,
    tenant: tenant ?? undefined,
    from: from ?? undefined,
    to: to ?? undefined,
    minDurationMicros: minDurationMicros ? Number(minDurationMicros) : undefined,
    page: page ? Number(page) : 1,
    pageSize: pageSize ? Number(pageSize) : DEFAULT_PAGE_SIZE,
  };
}

function paramsFromQuery(query: TraceQuery): Params {
  const params: Params = {};
  if (query.status) {
    params['status'] = query.status;
  }
  if (query.method) {
    params['method'] = query.method;
  }
  if (query.route) {
    params['route'] = query.route;
  }
  if (query.tenant) {
    params['tenant'] = query.tenant;
  }
  if (query.from) {
    params['from'] = query.from;
  }
  if (query.to) {
    params['to'] = query.to;
  }
  if (query.minDurationMicros !== undefined) {
    params['minDurationMicros'] = String(query.minDurationMicros);
  }
  if (query.page !== undefined) {
    params['page'] = String(query.page);
  }
  if (query.pageSize !== undefined) {
    params['pageSize'] = String(query.pageSize);
  }
  return params;
}

function countActiveFilters(query: TraceQuery): number {
  return [
    query.status,
    query.method,
    query.route,
    query.tenant,
    query.from,
    query.to,
    query.minDurationMicros,
  ].filter((value) => value !== undefined).length;
}

function msToMicros(ms: number): number {
  return Math.round(ms * 1000);
}

function microsToMs(micros: number): number {
  return micros / 1000;
}

function rowStatusState(trace: FlowTraceSummary): NodeVisualState {
  if (trace.status === 'Running') {
    return 'running';
  }
  return trace.httpStatus !== null && trace.httpStatus >= 400 ? 'error' : 'completed';
}

function rowStatusTint(state: NodeVisualState): 'flow' | 'danger' | 'success' {
  if (state === 'running') {
    return 'flow';
  }
  return state === 'error' ? 'danger' : 'success';
}

function describeError(error: HttpErrorResponse): string {
  return error.status ? `HTTP ${error.status} ${error.statusText}`.trim() : error.message;
}

/** Redesigned `/history` per the UX guide: filters card, results card, explicit empty/error states. */
@Component({
  selector: 'app-history',
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss',
  imports: [RouterLink, TranslatePipe],
})
export class HistoryComponent {
  private readonly api = inject(ApiService);
  private readonly activatedRoute = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly methods = METHODS;
  readonly periodPresets = PERIOD_PRESETS;
  readonly pageSizes = PAGE_SIZES;

  readonly filters = signal<TraceQuery>(
    queryFromParamMap(this.activatedRoute.snapshot.queryParamMap),
  );

  readonly routeText = signal(this.filters().route ?? '');
  readonly tenantText = signal(this.filters().tenant ?? '');
  readonly activePeriod = signal<PeriodPreset | 'custom' | null>(null);
  readonly customFrom = signal('');
  readonly customTo = signal('');
  readonly slowOnly = signal(false);
  readonly minDurationMsInput = signal<number | null>(
    this.filters().minDurationMicros !== undefined
      ? microsToMs(this.filters().minDurationMicros as number)
      : null,
  );

  readonly slowThresholdMicros = signal<number | null>(null);
  readonly filtersOpen = signal(false);

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly result = signal<PagedResult<FlowTraceSummary> | null>(null);

  readonly activeFilterCount = computed(() => countActiveFilters(this.filters()));

  readonly viewState = computed<ViewState>(() => {
    if (this.loading()) {
      return 'loading';
    }
    if (this.errorMessage()) {
      return 'error';
    }
    const page = this.result();
    if (page && page.items.length === 0) {
      return this.activeFilterCount() > 0 ? 'empty-filtered' : 'empty-none';
    }
    return 'data';
  });

  readonly activeCountKey = computed<TranslationKey>(() =>
    this.activeFilterCount() === 1
      ? 'history.filter.activeCount.one'
      : 'history.filter.activeCount.many',
  );

  readonly emptyFilteredBodyKey = computed<TranslationKey>(() =>
    this.activeFilterCount() === 1
      ? 'history.state.empty.filtered.body.one'
      : 'history.state.empty.filtered.body.many',
  );

  readonly pageRange = computed(() => {
    const page = this.result();
    const query = this.filters();
    const total = page?.totalCount ?? 0;
    const pageSize = query.pageSize ?? DEFAULT_PAGE_SIZE;
    const pageNumber = query.page ?? 1;
    const from = total === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
    const to = Math.min(pageNumber * pageSize, total);
    return { from, to, total };
  });

  readonly hasNextPage = computed(() => {
    const page = this.result();
    const query = this.filters();
    if (!page) {
      return false;
    }
    return (query.page ?? 1) * (query.pageSize ?? DEFAULT_PAGE_SIZE) < page.totalCount;
  });

  private readonly routeInput$ = new Subject<string>();
  private readonly tenantInput$ = new Subject<string>();

  constructor() {
    this.routeInput$
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((value) => this.patchFilters({ route: value || undefined }));

    this.tenantInput$
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((value) => this.patchFilters({ tenant: value || undefined }));

    this.api.getConfig().subscribe((config) => {
      this.slowThresholdMicros.set(config.slowThresholdMicros);
    });

    effect(() => {
      const query = this.filters();
      this.loading.set(true);
      this.errorMessage.set(null);

      this.api.getTraces(query).subscribe({
        next: (result) => {
          this.result.set(result);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.errorMessage.set(describeError(error));
          this.loading.set(false);
        },
      });
    });

    effect(() => {
      const query = this.filters();
      void this.router.navigate([], {
        relativeTo: this.activatedRoute,
        queryParams: paramsFromQuery(query),
        replaceUrl: true,
      });
    });
  }

  onRouteInput(event: Event): void {
    const value = inputValue(event);
    this.routeText.set(value);
    this.routeInput$.next(value);
  }

  onTenantInput(event: Event): void {
    const value = inputValue(event);
    this.tenantText.set(value);
    this.tenantInput$.next(value);
  }

  setStatus(status: FlowStatus | null): void {
    this.patchFilters({ status: status ?? undefined });
  }

  setMethod(method: string | null): void {
    this.patchFilters({ method: method ?? undefined });
  }

  selectPeriodPreset(preset: PeriodPreset): void {
    this.activePeriod.set(preset);
    this.customFrom.set('');
    this.customTo.set('');
    this.patchFilters({ from: periodPresetFrom(preset, new Date()), to: undefined });
  }

  selectCustomPeriod(): void {
    this.activePeriod.set('custom');
  }

  periodLabelKey(preset: PeriodPreset): TranslationKey {
    return `history.filter.period.${preset}` as TranslationKey;
  }

  toggleFiltersOpen(): void {
    this.filtersOpen.update((open) => !open);
  }

  onCustomFromChange(event: Event): void {
    const value = inputValue(event);
    this.customFrom.set(value);
    this.patchFilters({ from: value ? new Date(value).toISOString() : undefined });
  }

  onCustomToChange(event: Event): void {
    const value = inputValue(event);
    this.customTo.set(value);
    this.patchFilters({ to: value ? new Date(value).toISOString() : undefined });
  }

  toggleSlowOnly(): void {
    const next = !this.slowOnly();
    this.slowOnly.set(next);

    if (next) {
      this.patchFilters({ minDurationMicros: this.slowThresholdMicros() ?? undefined });
      return;
    }

    const ms = this.minDurationMsInput();
    this.patchFilters({ minDurationMicros: ms !== null ? msToMicros(ms) : undefined });
  }

  onMinDurationChange(event: Event): void {
    const value = inputValue(event);
    const ms = value === '' ? null : Number(value);
    this.minDurationMsInput.set(ms);
    this.patchFilters({ minDurationMicros: ms !== null ? msToMicros(ms) : undefined });
  }

  clearFilters(): void {
    this.routeText.set('');
    this.tenantText.set('');
    this.activePeriod.set(null);
    this.customFrom.set('');
    this.customTo.set('');
    this.slowOnly.set(false);
    this.minDurationMsInput.set(null);
    this.filters.set({ page: 1, pageSize: this.filters().pageSize });
  }

  setPage(page: number): void {
    this.filters.update((current) => ({ ...current, page }));
  }

  setPageSize(event: Event): void {
    const pageSize = Number(inputValue(event));
    this.filters.update((current) => ({ ...current, pageSize, page: 1 }));
  }

  retry(): void {
    this.filters.update((current) => ({ ...current }));
  }

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }

  isSlowDuration(micros: number | null): boolean {
    const threshold = this.slowThresholdMicros();
    return micros !== null && threshold !== null && micros >= threshold;
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  rowStateIconSrc(trace: FlowTraceSummary): string {
    return NODE_STATE_PRESENTATION[rowStatusState(trace)].iconSrc;
  }

  rowStateLabelKey(trace: FlowTraceSummary): TranslationKey {
    return NODE_STATE_PRESENTATION[rowStatusState(trace)].labelKey;
  }

  rowStatusTintClass(trace: FlowTraceSummary): string {
    return `history__status-pill--${rowStatusTint(rowStatusState(trace))}`;
  }

  startedAtShort(startedAt: string): string {
    return formatStartedAtShort(new Date(startedAt));
  }

  startedAtFull(startedAt: string): string {
    return formatStartedAtFull(new Date(startedAt));
  }

  shortTraceId(traceId: string): string {
    return traceId.slice(0, 8);
  }

  copyTraceId(traceId: string, event: Event): void {
    event.stopPropagation();
    void navigator.clipboard?.writeText(traceId);
  }

  private patchFilters(patch: Partial<TraceQuery>): void {
    this.filters.update((current) => ({ ...current, ...patch, page: 1 }));
  }
}
