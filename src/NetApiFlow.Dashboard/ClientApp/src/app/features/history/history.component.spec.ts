import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HistoryComponent } from './history.component';
import { PagedResult, FlowTraceSummary } from '../../core/models';

const EMPTY_PAGE: PagedResult<FlowTraceSummary> = { items: [], totalCount: 0 };

describe('HistoryComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('fetches page 1 with the default page size on first render', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/traces' &&
        request.params.get('page') === '1' &&
        request.params.get('pageSize') === '50',
    );
    req.flush(EMPTY_PAGE);
  });

  it('refetches with the status filter applied, resetting to page 1', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);

    fixture.componentInstance.onStatusChange({ target: { value: 'Error' } } as unknown as Event);
    await fixture.whenStable();

    const req = httpMock.expectOne(
      (request) => request.url === 'api/traces' && request.params.get('status') === 'Error',
    );
    expect(req.request.params.get('page')).toBe('1');
    req.flush(EMPTY_PAGE);
  });

  it('refetches with each filter applied in isolation', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);

    fixture.componentInstance.onMethodChange({ target: { value: 'POST' } } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('method') === 'POST').flush(EMPTY_PAGE);

    fixture.componentInstance.onRouteChange({
      target: { value: '/api/orders/{id}' },
    } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('route') === '/api/orders/{id}').flush(EMPTY_PAGE);

    fixture.componentInstance.onTenantChange({ target: { value: 'acme' } } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('tenant') === 'acme').flush(EMPTY_PAGE);

    fixture.componentInstance.onFromChange({
      target: { value: '2026-01-01T00:00' },
    } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('from') === '2026-01-01T00:00').flush(EMPTY_PAGE);

    fixture.componentInstance.onToChange({
      target: { value: '2026-01-02T00:00' },
    } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('to') === '2026-01-02T00:00').flush(EMPTY_PAGE);

    fixture.componentInstance.onMinDurationChange({
      target: { value: '1000' },
    } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne((r) => r.params.get('minDurationMicros') === '1000').flush(EMPTY_PAGE);
  });

  it('combines multiple filters into the same request', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);

    fixture.componentInstance.onStatusChange({ target: { value: 'Success' } } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);

    fixture.componentInstance.onMethodChange({ target: { value: 'GET' } } as unknown as Event);
    await fixture.whenStable();

    const req = httpMock.expectOne(
      (request) =>
        request.params.get('status') === 'Success' && request.params.get('method') === 'GET',
    );
    req.flush(EMPTY_PAGE);
  });

  it('moves to the next page without resetting the active filters', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(EMPTY_PAGE);

    fixture.componentInstance.onMethodChange({ target: { value: 'GET' } } as unknown as Event);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush({ items: [], totalCount: 120 });

    fixture.componentInstance.setPage(2);
    await fixture.whenStable();

    const req = httpMock.expectOne(
      (request) => request.params.get('page') === '2' && request.params.get('method') === 'GET',
    );
    req.flush(EMPTY_PAGE);
  });

  it('shows an explicit error state when the request fails', async () => {
    const fixture = TestBed.createComponent(HistoryComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Failed to load traces.');
  });
});
