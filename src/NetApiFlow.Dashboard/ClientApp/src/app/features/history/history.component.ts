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
  SortDirection,
  TraceQuery,
  TraceSortField,
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
const SORTABLE_FIELDS: readonly TraceSortField[] = [
  'httpStatus',
  'method',
  'route',
  'startedAt',
  'duration',
];
const DEFAULT_SORT_FIELD: TraceSortField = 'startedAt';
const DEFAULT_SORT_DIRECTION: SortDirection = 'desc';

const SORT_FIELD_DEFAULT_DIRECTION: Readonly<Record<TraceSortField, SortDirection>> = {
  startedAt: 'desc',
  duration: 'desc',
  httpStatus: 'asc',
  method: 'asc',
  route: 'asc',
};

const SORT_FIELD_LABEL_KEY: Readonly<Record<TraceSortField, TranslationKey>> = {
  startedAt: 'history.table.startedAt',
  duration: 'history.table.duration',
  httpStatus: 'history.table.status',
  method: 'history.table.method',
  route: 'history.table.route',
};

const MOBILE_SORT_OPTIONS: readonly { field: TraceSortField; direction: SortDirection }[] = [
  { field: 'startedAt', direction: 'desc' },
  { field: 'startedAt', direction: 'asc' },
  { field: 'duration', direction: 'desc' },
  { field: 'duration', direction: 'asc' },
  { field: 'httpStatus', direction: 'asc' },
  { field: 'httpStatus', direction: 'desc' },
  { field: 'method', direction: 'asc' },
  { field: 'method', direction: 'desc' },
  { field: 'route', direction: 'asc' },
  { field: 'route', direction: 'desc' },
];

type ViewState = 'loading' | 'error' | 'empty-none' | 'empty-filtered' | 'data';

function inputValue(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}

function isFlowStatus(value: string | null): value is FlowStatus {
  return (
    value === 'Running' ||
    value === 'Success' ||
    value === 'Error' ||
    value === 'Cancelled' ||
    value === 'Timeout'
  );
}

function isTraceSortField(value: string | null): value is TraceSortField {
  return SORTABLE_FIELDS.includes(value as TraceSortField);
}

function isSortDirection(value: string | null): value is SortDirection {
  return value === 'asc' || value === 'desc';
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
  const sort = params.get('sort');
  const order = params.get('order');

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
    sort: isTraceSortField(sort) ? sort : undefined,
    order: isSortDirection(order) ? order : undefined,
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
  if (query.sort !== undefined) {
    params['sort'] = query.sort;
  }
  if (query.order !== undefined) {
    params['order'] = query.order;
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
  if (trace.status === 'Cancelled') {
    return 'cancelled';
  }
  if (trace.status === 'Timeout') {
    return 'timeout';
  }
  return trace.httpStatus !== null && trace.httpStatus >= 400 ? 'error' : 'completed';
}

function rowStatusTint(state: NodeVisualState): 'flow' | 'danger' | 'success' | 'idle' {
  if (state === 'running') {
    return 'flow';
  }
  if (state === 'cancelled') {
    return 'idle';
  }
  return state === 'error' || state === 'timeout' ? 'danger' : 'success';
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
  readonly tailSamplingEnabled = signal(false);
  readonly successSampleRate = signal(0);
  readonly filtersOpen = signal(false);

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly result = signal<PagedResult<FlowTraceSummary> | null>(null);

  /** At most 2, in selection order: a 3rd pick drops the oldest (FIFO), same as a product compare picker. */
  readonly selectedTraces = signal<FlowTraceSummary[]>([]);
  readonly canCompare = computed(() => this.selectedTraces().length === 2);
  readonly compareRouteMismatch = computed(() => {
    const [a, b] = this.selectedTraces();
    return !!a && !!b && (a.method !== b.method || a.routeTemplate !== b.routeTemplate);
  });

  readonly activeFilterCount = computed(() => countActiveFilters(this.filters()));

  readonly tailSamplingRateLabel = computed(() => `${Math.round(this.successSampleRate() * 100)}%`);

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

  readonly sortableFields = SORTABLE_FIELDS;
  readonly mobileSortOptions = MOBILE_SORT_OPTIONS;

  readonly activeSort = computed<TraceSortField>(() => this.filters().sort ?? DEFAULT_SORT_FIELD);
  readonly activeOrder = computed<SortDirection>(
    () => this.filters().order ?? DEFAULT_SORT_DIRECTION,
  );

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
      this.tailSamplingEnabled.set(config.tailSamplingEnabled);
      this.successSampleRate.set(config.successSampleRate);
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

  toggleSort(field: TraceSortField): void {
    const order = this.isActiveSort(field)
      ? this.activeOrder() === 'asc'
        ? 'desc'
        : 'asc'
      : SORT_FIELD_DEFAULT_DIRECTION[field];
    this.patchFilters({ sort: field, order });
  }

  isActiveSort(field: TraceSortField): boolean {
    return this.activeSort() === field;
  }

  sortAriaSort(field: TraceSortField): 'ascending' | 'descending' | 'none' {
    if (!this.isActiveSort(field)) {
      return 'none';
    }
    return this.activeOrder() === 'asc' ? 'ascending' : 'descending';
  }

  sortColumnKey(field: TraceSortField): TranslationKey {
    return SORT_FIELD_LABEL_KEY[field];
  }

  sortIndicatorDirectionKey(field: TraceSortField): TranslationKey {
    const direction = this.isActiveSort(field)
      ? this.activeOrder()
      : SORT_FIELD_DEFAULT_DIRECTION[field];
    return this.directionLabelKey(direction);
  }

  directionLabelKey(direction: SortDirection): TranslationKey {
    return direction === 'asc' ? 'history.sort.ascending' : 'history.sort.descending';
  }

  mobileSortOptionValue(option: { field: TraceSortField; direction: SortDirection }): string {
    return `${option.field}:${option.direction}`;
  }

  isMobileSortOptionActive(option: { field: TraceSortField; direction: SortDirection }): boolean {
    return this.activeSort() === option.field && this.activeOrder() === option.direction;
  }

  onMobileSortChange(event: Event): void {
    const [field, direction] = inputValue(event).split(':') as [TraceSortField, SortDirection];
    this.patchFilters({ sort: field, order: direction });
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

  isSelected(traceId: string): boolean {
    return this.selectedTraces().some((trace) => trace.traceId === traceId);
  }

  toggleSelection(trace: FlowTraceSummary): void {
    const current = this.selectedTraces();
    if (this.isSelected(trace.traceId)) {
      this.selectedTraces.set(current.filter((selected) => selected.traceId !== trace.traceId));
      return;
    }

    const next = [...current, trace];
    this.selectedTraces.set(next.length > 2 ? next.slice(1) : next);
  }

  compare(): void {
    const [a, b] = this.selectedTraces();
    if (!a || !b) {
      return;
    }

    void this.router.navigate(['/diff'], { queryParams: { a: a.traceId, b: b.traceId } });
  }

  private patchFilters(patch: Partial<TraceQuery>): void {
    this.filters.update((current) => ({ ...current, ...patch, page: 1 }));
  }
}
