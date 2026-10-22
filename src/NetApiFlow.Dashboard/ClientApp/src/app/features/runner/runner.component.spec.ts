import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';
import { RunnerComponent } from './runner.component';
import { OpenApiDocument } from '../../core/models';
import { SseService, SseConnectionState } from '../../core/sse.service';

const fakeSse = {
  events$: new Subject().asObservable(),
  reconnected$: new Subject<void>().asObservable(),
  state: () => 'open' as SseConnectionState,
  connect: vi.fn(),
  disconnect: vi.fn(),
};

const DOC: OpenApiDocument = { paths: {} };

describe('RunnerComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SseService, useValue: fakeSse },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('tracks whatever trace the embedded endpoint form dispatches', async () => {
    const fixture = TestBed.createComponent(RunnerComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.onTraceDispatched('t1');

    httpMock.expectOne('api/traces/t1').flush({
      traceId: 't1',
      method: 'POST',
      routeTemplate: '/api/orders/{id}',
      httpStatus: 201,
      status: 'Success',
      startedAt: '2026-01-01T00:00:00.000000Z',
      endedAt: '2026-01-01T00:00:00.000100Z',
      durationMicros: 100,
      spanCount: 0,
      truncated: false,
      errorMessage: null,
      tags: {},
      spans: [],
    });
    await fixture.whenStable();

    expect(fixture.componentInstance.livePlayer.trace()?.traceId).toBe('t1');
  });
});
