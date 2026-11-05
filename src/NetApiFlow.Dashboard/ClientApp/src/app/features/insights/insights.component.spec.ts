import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { InsightsComponent } from './insights.component';
import { EndpointDescriptor, RouteStat } from '../../core/models';

const ROUTE_STATS: RouteStat[] = [
  {
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    executionCount: 612,
    errorCount: 9,
    medianDurationMicros: 18_200,
    averageDurationMicros: 21_500,
    p95DurationMicros: 88_200,
    maxDurationMicros: 412_000,
  },
  {
    method: 'POST',
    routeTemplate: '/api/orders',
    executionCount: 40,
    errorCount: 0,
    medianDurationMicros: 5_000,
    averageDurationMicros: 5_500,
    p95DurationMicros: 9_000,
    maxDurationMicros: 12_000,
  },
];

const ENDPOINTS: EndpointDescriptor[] = [
  { method: 'GET', routeTemplate: '/api/orders/{id}', displayName: 'GET /api/orders/{id}' },
  { method: 'POST', routeTemplate: '/api/orders', displayName: 'POST /api/orders' },
  { method: 'DELETE', routeTemplate: '/api/orders/{id}', displayName: 'DELETE /api/orders/{id}' },
];

function flushInitial(
  httpMock: HttpTestingController,
  stats: RouteStat[],
  endpoints: EndpointDescriptor[],
): void {
  httpMock.expectOne((req) => req.url === 'api/metrics/routes').flush(stats);
  httpMock.expectOne((req) => req.url === 'api/endpoints').flush(endpoints);
}

describe('InsightsComponent', () => {
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

  it('shows the slowest routes ordered by p95 duration descending', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    flushInitial(httpMock, ROUTE_STATS, ENDPOINTS);
    await fixture.whenStable();

    const rows = fixture.nativeElement
      .querySelectorAll('.insights__block')[0]
      .querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].querySelectorAll('td')[1].textContent?.trim()).toBe('/api/orders/{id}');
  });

  it('shows only routes with errors in the top errors block', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    flushInitial(httpMock, ROUTE_STATS, ENDPOINTS);
    await fixture.whenStable();

    const rows = fixture.nativeElement
      .querySelectorAll('.insights__block')[1]
      .querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].querySelectorAll('td')[1].textContent?.trim()).toBe('/api/orders/{id}');
  });

  it('lists dead routes: endpoints with no matching route stat in the window', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    flushInitial(httpMock, ROUTE_STATS, ENDPOINTS);
    await fixture.whenStable();

    const rows = fixture.nativeElement
      .querySelectorAll('.insights__block')[2]
      .querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].querySelectorAll('td')[0].textContent?.trim()).toBe('DELETE');
    expect(rows[0].querySelectorAll('td')[1].textContent?.trim()).toBe('/api/orders/{id}');
  });

  it('shows an explicit empty state for dead routes when every route ran', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    flushInitial(httpMock, ROUTE_STATS, ENDPOINTS.slice(0, 2));
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain(
      'Toda rota rastreável foi executada nesta janela.',
    );
  });

  it('shows an explicit error state when the request fails', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints');
    httpMock
      .expectOne((req) => req.url === 'api/metrics/routes')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar os insights.');
  });

  it('refetches both routes and endpoints with the expected from when the window changes', async () => {
    const fixture = TestBed.createComponent(InsightsComponent);
    await fixture.whenStable();
    flushInitial(httpMock, ROUTE_STATS, ENDPOINTS);
    await fixture.whenStable();

    fixture.componentInstance.selectWindow('24h');
    await fixture.whenStable();

    const routesReq = httpMock.expectOne((req) => req.url === 'api/metrics/routes');
    const from = new Date(routesReq.request.params.get('from') as string);
    const hoursAgo = (Date.now() - from.getTime()) / (60 * 60 * 1000);
    expect(hoursAgo).toBeGreaterThan(23);
    expect(hoursAgo).toBeLessThan(25);
    routesReq.flush([]);
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush([]);
  });
});
