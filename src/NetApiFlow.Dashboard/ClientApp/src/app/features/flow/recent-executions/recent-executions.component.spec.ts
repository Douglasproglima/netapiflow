import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { RecentExecutionsComponent } from './recent-executions.component';
import { FlowEvent, FlowTraceSummary, PagedResult } from '../../../core/models';
import { SseService } from '../../../core/sse.service';

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

function fakeSseService() {
  const events = new Subject<FlowEvent>();
  return { events$: events.asObservable(), emit: (event: FlowEvent) => events.next(event) };
}

describe('RecentExecutionsComponent', () => {
  let httpMock: HttpTestingController;
  let sse: ReturnType<typeof fakeSseService>;

  beforeEach(() => {
    sse = fakeSseService();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SseService, useValue: sse },
      ],
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

  it('re-fetches the list when a TraceCompleted event arrives (D-39)', async () => {
    const fixture = TestBed.createComponent(RecentExecutionsComponent);
    await fixture.whenStable();
    httpMock.expectOne(() => true).flush(PAGE);
    await fixture.whenStable();

    expect(fixture.componentInstance.traces()).toEqual(PAGE.items);

    const secondPage: PagedResult<FlowTraceSummary> = {
      totalCount: 2,
      items: [
        {
          traceId: 't2',
          method: 'POST',
          routeTemplate: '/api/orders/{id}/confirm',
          httpStatus: 500,
          status: 'Error',
          startedAt: '2026-01-01T00:01:00.000000Z',
          durationMicros: 2_000,
          truncated: false,
        },
        ...PAGE.items,
      ],
    };

    sse.emit({
      eventId: 1,
      type: 'TraceCompleted',
      traceId: 't2',
      timestamp: '2026-01-01T00:01:00Z',
    });
    httpMock.expectOne(() => true).flush(secondPage);
    await fixture.whenStable();

    expect(fixture.componentInstance.traces()).toEqual(secondPage.items);
  });
});
