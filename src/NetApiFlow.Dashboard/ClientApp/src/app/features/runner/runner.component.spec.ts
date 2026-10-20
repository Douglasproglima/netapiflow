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

const DOC: OpenApiDocument = {
  paths: {
    '/api/orders/{id}': {
      post: {
        operationId: 'CreateOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        requestBody: {
          required: true,
          content: { 'application/json': { schema: { type: 'object' } } },
        },
      },
    },
  },
};

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

  it('builds the expected form for an endpoint with a path param and a body', async () => {
    const fixture = TestBed.createComponent(RunnerComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    await fixture.whenStable();

    expect(fixture.componentInstance.form()).toEqual({
      method: 'POST',
      path: '/api/orders/{id}',
      operationId: 'CreateOrder',
      hasBody: true,
      fields: [
        { name: 'id', kind: 'path', type: 'integer', required: true },
        { name: 'body', kind: 'body', type: 'object', required: true },
      ],
    });
  });

  it('shows an explicit error state when the OpenAPI document fails to load', async () => {
    const fixture = TestBed.createComponent(RunnerComponent);
    await fixture.whenStable();
    httpMock
      .expectOne('/openapi/v1.json')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Failed to load the OpenAPI document.');
  });

  it('dispatches the filled form and tracks the returned trace id', async () => {
    const fixture = TestBed.createComponent(RunnerComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/orders/5');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ customerName: 'Acme' });
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    await fixture.whenStable();

    expect(fixture.componentInstance.lastTraceId()).toBe('t1');

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
  });
});
