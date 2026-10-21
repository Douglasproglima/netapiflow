import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RecentExecutionsComponent } from './recent-executions.component';
import { FlowTraceSummary, PagedResult } from '../../../core/models';

const PAGE: PagedResult<FlowTraceSummary> = {
  totalCount: 1,
  items: [
    {
      traceId: 't1',
      method: 'GET',
      routeTemplate: '/api/orders/{id}',
      httpStatus: 200,
      status: 'Success',
      startedAt: '2026-01-01T00:00:00.000000Z',
      durationMicros: 1_200,
      truncated: false,
    },
  ],
};

describe('RecentExecutionsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('fetches only the first page of GET /traces', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/traces' &&
        request.params.get('page') === '1' &&
        request.params.get('pageSize') === '8',
    );
    req.flush(PAGE);
    await fixture.whenStable();

    expect(fixture.componentInstance.traces()).toEqual(PAGE.items);
  });

  it('emits traceSelected when a row is clicked', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(PAGE);
    await fixture.whenStable();

    let selected: string | null = null;
    fixture.componentInstance.traceSelected.subscribe((traceId) => (selected = traceId));

    fixture.nativeElement.querySelector('.recent-executions__item').click();

    expect(selected).toBe('t1');
  });

  it('shows an explicit empty state when there are no traces', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush({ totalCount: 0, items: [] });
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain('Nenhum trace registrado ainda.');
  });

  it('shows the HTTP status code tinted by the trace status', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(PAGE);
    await fixture.whenStable();

    const status = fixture.nativeElement.querySelector('.recent-executions__status-text');
    expect(status.textContent).toContain('200');
    expect(status.className).toContain('recent-executions__status-text--success');
  });

  it('links "View all" to /history', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(PAGE);
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('a[href="/history"]')).toBeTruthy();
  });

  it('toggles collapsed state', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(PAGE);
    await fixture.whenStable();

    expect(fixture.componentInstance.collapsed()).toBe(false);
    fixture.componentInstance.toggleCollapsed();
    expect(fixture.componentInstance.collapsed()).toBe(true);
    fixture.componentInstance.toggleCollapsed();
    expect(fixture.componentInstance.collapsed()).toBe(false);
  });
});
