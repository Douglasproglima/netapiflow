import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HistoryComponent } from './history.component';
import { PagedResult, FlowTraceSummary } from '../../core/models';

@Component({ template: '', selector: 'app-diff-dummy' })
class DiffDummyComponent {}

const EMPTY_PAGE: PagedResult<FlowTraceSummary> = { items: [], totalCount: 0 };

function makeSummary(traceId: string, overrides: Partial<FlowTraceSummary> = {}): FlowTraceSummary {
  return {
    traceId,
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    durationMicros: 100,
    truncated: false,
    ...overrides,
  };
}
const CONFIG = {
  slowThresholdMicros: 250_000,
  version: '0.1.0',
  tailSamplingEnabled: false,
  successSampleRate: 0.1,
};

describe('HistoryComponent', () => {
  let httpMock: HttpTestingController;
  let harness: RouterTestingHarness;

  async function mount(url = '/history'): Promise<HistoryComponent> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'history', component: HistoryComponent },
          { path: 'diff', component: DiffDummyComponent },
        ]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create(url);
    return harness.routeDebugElement!.componentInstance as HistoryComponent;
  }

  function flushConfig(config = CONFIG): void {
    httpMock.expectOne('api/config').flush(config);
  }

  function flushTraces(page: PagedResult<FlowTraceSummary> = EMPTY_PAGE) {
    return httpMock.expectOne((request) => request.url === 'api/traces').flush(page);
  }

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('reads the initial filters from the query string and fetches page 1', async () => {
    const component = await mount('/history?status=Error&method=POST');
    flushConfig();

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/traces' &&
        request.params.get('status') === 'Error' &&
        request.params.get('method') === 'POST' &&
        request.params.get('page') === '1',
    );
    req.flush(EMPTY_PAGE);
    await harness.fixture.whenStable();

    expect(component.filters().status).toBe('Error');
    expect(component.filters().method).toBe('POST');
  });

  it('turns an emptied route/tenant input into no filter at all (H6)', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    component.onRouteInput({ target: { value: '/api/orders' } } as unknown as Event);
    vi.advanceTimersByTime(300);
    await Promise.resolve();
    harness.detectChanges();
    httpMock.expectOne((r) => r.params.get('route') === '/api/orders').flush(EMPTY_PAGE);
    await Promise.resolve();
    harness.detectChanges();

    component.onRouteInput({ target: { value: '' } } as unknown as Event);
    vi.advanceTimersByTime(300);
    await Promise.resolve();
    harness.detectChanges();

    const req = httpMock.expectOne(() => true);
    expect(req.request.params.has('route')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('"slow only" sends minDurationMicros equal to the configured threshold', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSlowOnly();
    await harness.fixture.whenStable();

    const req = httpMock.expectOne((r) => r.params.get('minDurationMicros') === '250000');
    req.flush(EMPTY_PAGE);
  });

  it('converts the min-duration field from ms to microseconds', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.onMinDurationChange({ target: { value: '250' } } as unknown as Event);
    await harness.fixture.whenStable();

    const req = httpMock.expectOne((r) => r.params.get('minDurationMicros') === '250000');
    req.flush(EMPTY_PAGE);
  });

  it('computes `from` for a period preset off the current clock', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    vi.setSystemTime(new Date('2026-10-21T17:30:00.000Z'));
    component.selectPeriodPreset('1h');
    await Promise.resolve();
    harness.detectChanges();

    const req = httpMock.expectOne((r) => r.params.get('from') === '2026-10-21T16:30:00.000Z');
    expect(req.request.params.has('to')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('resets to page 1 whenever a filter changes', async () => {
    const component = await mount();
    flushConfig();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setPage(3);
    await harness.fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('page') === '3').flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setStatus('Error');
    await harness.fixture.whenStable();

    const req = httpMock.expectOne((r) => r.params.get('status') === 'Error');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(EMPTY_PAGE);
  });

  it('shows the "no executions yet" message only when there is no active filter', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    expect(component.viewState()).toBe('empty-none');
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Nenhuma execução registrada ainda');
  });

  it('shows the "no match" message when a filter is active and the page is empty', async () => {
    const component = await mount('/history?status=Error');
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    expect(component.viewState()).toBe('empty-filtered');
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Nenhuma execução com esses filtros');
  });

  it('shows an explicit error state with the underlying message when the request fails', async () => {
    await mount();
    flushConfig();
    httpMock
      .expectOne(() => true)
      .flush('boom', { status: 503, statusText: 'Service Unavailable' });
    await harness.fixture.whenStable();

    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain(
      'Não foi possível carregar o histórico',
    );
    expect(harness.routeNativeElement?.textContent).toContain('503');
  });

  it('moves to the next page without resetting the active filters', async () => {
    const component = await mount();
    flushConfig();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setMethod('GET');
    await harness.fixture.whenStable();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setPage(2);
    await harness.fixture.whenStable();

    const req = httpMock.expectOne(
      (request) => request.params.get('page') === '2' && request.params.get('method') === 'GET',
    );
    req.flush(EMPTY_PAGE);
  });

  it('changing the page size resets to page 1', async () => {
    const component = await mount();
    flushConfig();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setPage(2);
    await harness.fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('page') === '2').flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setPageSize({ target: { value: '20' } } as unknown as Event);
    await harness.fixture.whenStable();

    const req = httpMock.expectOne(
      (request) => request.params.get('pageSize') === '20' && request.params.get('page') === '1',
    );
    req.flush(EMPTY_PAGE);
  });

  it('first click on a column sorts duration/startedAt descending and the rest ascending', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSort('duration');
    await harness.fixture.whenStable();
    httpMock
      .expectOne((r) => r.params.get('sort') === 'duration' && r.params.get('order') === 'desc')
      .flush(EMPTY_PAGE);
    await harness.fixture.whenStable();

    component.toggleSort('route');
    await harness.fixture.whenStable();
    const req = httpMock.expectOne(
      (r) => r.params.get('sort') === 'route' && r.params.get('order') === 'asc',
    );
    req.flush(EMPTY_PAGE);
  });

  it('clicking the active column again flips its direction', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSort('duration');
    await harness.fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('order') === 'desc').flush(EMPTY_PAGE);
    await harness.fixture.whenStable();

    component.toggleSort('duration');
    await harness.fixture.whenStable();

    const req = httpMock.expectOne(
      (r) => r.params.get('sort') === 'duration' && r.params.get('order') === 'asc',
    );
    req.flush(EMPTY_PAGE);
  });

  it('reflects the active sort column and direction in aria-sort', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    expect(component.sortAriaSort('startedAt')).toBe('descending');
    expect(component.sortAriaSort('duration')).toBe('none');

    component.toggleSort('duration');
    await harness.fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);
    await harness.fixture.whenStable();

    expect(component.sortAriaSort('duration')).toBe('descending');
    expect(component.sortAriaSort('startedAt')).toBe('none');
  });

  it('sorting resets to page 1 and keeps the active filters', async () => {
    const component = await mount();
    flushConfig();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setMethod('GET');
    await harness.fixture.whenStable();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.setPage(2);
    await harness.fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('page') === '2').flush({ items: [], totalCount: 200 });
    await harness.fixture.whenStable();

    component.toggleSort('duration');
    await harness.fixture.whenStable();

    const req = httpMock.expectOne(
      (r) =>
        r.params.get('sort') === 'duration' &&
        r.params.get('page') === '1' &&
        r.params.get('method') === 'GET',
    );
    req.flush(EMPTY_PAGE);
  });

  it('reads a valid sort/order from the query string on mount', async () => {
    const valid = await mount('/history?sort=duration&order=asc');
    flushConfig();
    httpMock
      .expectOne((r) => r.params.get('sort') === 'duration' && r.params.get('order') === 'asc')
      .flush(EMPTY_PAGE);
    await harness.fixture.whenStable();
    expect(valid.filters().sort).toBe('duration');
    expect(valid.filters().order).toBe('asc');
  });

  it('falls back to the default sort when the query string has an invalid value', async () => {
    const invalid = await mount('/history?sort=bogus&order=sideways');
    flushConfig();
    httpMock.expectOne((r) => !r.params.has('sort') && !r.params.has('order')).flush(EMPTY_PAGE);
    await harness.fixture.whenStable();
    expect(invalid.filters().sort).toBeUndefined();
    expect(invalid.filters().order).toBeUndefined();
    expect(invalid.activeSort()).toBe('startedAt');
    expect(invalid.activeOrder()).toBe('desc');
  });

  it('shows the tail-sampling banner when the backend reports it enabled', async () => {
    await mount();
    flushConfig({ ...CONFIG, tailSamplingEnabled: true, successSampleRate: 0.1 });
    flushTraces();
    await harness.fixture.whenStable();
    harness.detectChanges();

    const banner = harness.fixture.nativeElement.querySelector('.history__tail-sampling-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain('10%');
  });

  it('hides the tail-sampling banner when the backend reports it disabled', async () => {
    await mount();
    flushConfig({ ...CONFIG, tailSamplingEnabled: false });
    flushTraces();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(
      harness.fixture.nativeElement.querySelector('.history__tail-sampling-banner'),
    ).toBeNull();
  });

  it('cannot compare with fewer than 2 executions selected', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1'));

    expect(component.canCompare()).toBe(false);
  });

  it('can compare once exactly 2 executions are selected', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1'));
    component.toggleSelection(makeSummary('t2'));

    expect(component.canCompare()).toBe(true);
    expect(component.selectedTraces().map((trace) => trace.traceId)).toEqual(['t1', 't2']);
  });

  it('drops the oldest selection (FIFO) when a 3rd execution is picked', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1'));
    component.toggleSelection(makeSummary('t2'));
    component.toggleSelection(makeSummary('t3'));

    expect(component.selectedTraces().map((trace) => trace.traceId)).toEqual(['t2', 't3']);
    expect(component.canCompare()).toBe(true);
  });

  it('unselects an execution clicked a second time', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1'));
    component.toggleSelection(makeSummary('t1'));

    expect(component.selectedTraces()).toEqual([]);
    expect(component.isSelected('t1')).toBe(false);
  });

  it('warns (without blocking) when the two selected executions are not the same route', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1', { routeTemplate: '/api/orders/{id}' }));
    component.toggleSelection(makeSummary('t2', { method: 'POST', routeTemplate: '/api/orders' }));

    expect(component.compareRouteMismatch()).toBe(true);
    expect(component.canCompare()).toBe(true);
  });

  it('navigates to /diff with both trace ids when comparing', async () => {
    const component = await mount();
    flushConfig();
    flushTraces();
    await harness.fixture.whenStable();

    component.toggleSelection(makeSummary('t1'));
    component.toggleSelection(makeSummary('t2'));
    component.compare();
    await harness.fixture.whenStable();

    const url = TestBed.inject(Router).url;
    expect(url).toContain('/diff');
    expect(url).toContain('a=t1');
    expect(url).toContain('b=t2');
  });
});
