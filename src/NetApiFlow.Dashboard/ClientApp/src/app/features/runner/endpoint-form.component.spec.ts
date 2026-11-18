import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { EndpointFormComponent } from './endpoint-form.component';
import { DashboardConfig, OpenApiDocument } from '../../core/models';
import { RunnerCredentialsService } from '../../core/runner-credentials.service';

const CONFIG: DashboardConfig = {
  slowThresholdMicros: 250_000,
  version: '0.1.0',
  tailSamplingEnabled: false,
  successSampleRate: 0.1,
  openApiDocumentPath: '/openapi/v1.json',
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

const DOC_WITH_SECURITY: OpenApiDocument = {
  components: {
    securitySchemes: {
      Bearer: { type: 'http', scheme: 'bearer' },
      ApiKeyScheme: { type: 'apiKey', in: 'header', name: 'X-Api-Key' },
      OAuth2Scheme: { type: 'oauth2' },
    },
  },
  paths: {
    '/api/orders/{id}': {
      get: {
        operationId: 'GetOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        security: [{ Bearer: [] }, { ApiKeyScheme: [] }],
      },
      delete: {
        operationId: 'DeleteOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        security: [],
      },
      post: {
        operationId: 'UndeclaredOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
      },
    },
  },
};

const DOC_WITH_UNAPPLIABLE_SECURITY: OpenApiDocument = {
  components: {
    securitySchemes: {
      BasicScheme: { type: 'http', scheme: 'basic' },
    },
  },
  paths: {
    '/api/orders/{id}': {
      get: {
        operationId: 'GetOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        security: [{ BasicScheme: [] }],
      },
    },
  },
};

function jwtExpiringInSeconds(seconds: number): string {
  const exp = Math.floor(Date.now() / 1000) + seconds;
  const payload = btoa(JSON.stringify({ sub: 'user', exp })).replace(/=+$/, '');
  return `header.${payload}.signature`;
}

describe('EndpointFormComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  async function createComponentWithConfig(
    config: Partial<DashboardConfig> = {},
  ): Promise<ComponentFixture<EndpointFormComponent>> {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush({ ...CONFIG, ...config });
    await fixture.whenStable();
    return fixture;
  }

  it('builds the expected form for an endpoint with a path param and a body', async () => {
    const fixture = await createComponentWithConfig();
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
    const fixture = await createComponentWithConfig();
    httpMock
      .expectOne('/openapi/v1.json')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // pt-BR is the default language.
    expect(fixture.nativeElement.textContent).toContain(
      'Não foi possível carregar o documento OpenAPI.',
    );
  });

  it('reads the OpenAPI document from the path the host configured', async () => {
    const fixture = await createComponentWithConfig({
      openApiDocumentPath: '/swagger/v1/swagger.json',
    });
    httpMock.expectOne('/swagger/v1/swagger.json').flush(DOC);
    await fixture.whenStable();

    expect(fixture.componentInstance.endpoints()).toHaveLength(1);
  });

  it('falls back to the default OpenAPI path when the config request fails', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    expect(fixture.componentInstance.endpoints()).toHaveLength(1);
  });

  it('filters the endpoint list by route', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.setRouteFilter('orders');
    expect(fixture.componentInstance.endpoints()).toHaveLength(1);

    fixture.componentInstance.setRouteFilter('nothing-matches');
    expect(fixture.componentInstance.endpoints()).toHaveLength(0);
  });

  it('dispatches the filled form and emits the returned trace id', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    let dispatchedTraceId: string | null = null;
    fixture.componentInstance.traceDispatched.subscribe((traceId) => (dispatchedTraceId = traceId));

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
    expect(dispatchedTraceId).toBe('t1');
  });

  it('sends a configured custom header on the dispatched request', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.addHeader();
    fixture.componentInstance.setHeaderName(0, 'X-Tenant');
    fixture.componentInstance.setHeaderValue(0, 'acme');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/orders/5');
    expect(req.request.headers.get('X-Tenant')).toBe('acme');
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
  });

  it('ignores a header row left with a blank name', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.addHeader();
    fixture.componentInstance.setHeaderValue(0, 'acme');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/orders/5');
    expect(req.request.headers.keys()).toEqual([]);
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
  });

  it('blocks the send and flags the empty field when a path param is left blank', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.send();
    await fixture.whenStable();

    expect(fixture.componentInstance.invalidFieldName()).toBe('id');
    // pt-BR is the default language.
    expect(fixture.nativeElement.textContent).toContain(
      'Preencha o parâmetro de rota "id" antes de enviar.',
    );
    httpMock.expectNone('/api/orders/');
  });

  it('never renders a Scenarios tab or any hardcoded sample route', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    const nativeElement: HTMLElement = fixture.nativeElement;
    expect(nativeElement.querySelector('[role="tab"]')).toBeNull();
    expect(nativeElement.querySelector('[role="tablist"]')).toBeNull();
    expect(nativeElement.textContent).not.toContain('/api/orders/1');
  });

  it('keeps a custom header filled in after switching to a different endpoint', async () => {
    const credentials = TestBed.inject(RunnerCredentialsService);
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.addHeader();
    fixture.componentInstance.setHeaderName(0, 'X-Tenant');
    fixture.componentInstance.setHeaderValue(0, 'acme');

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');

    expect(credentials.customHeaders()).toEqual([{ name: 'X-Tenant', value: 'acme' }]);
  });

  it('reconciles saved credentials against the newly loaded document once the fetch succeeds', async () => {
    const credentials = TestBed.inject(RunnerCredentialsService);
    credentials.setAuthValue('RetiredScheme', 'stale-token');

    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    expect(credentials.authValue('RetiredScheme')).toBe('');
  });

  it('never reconciles saved credentials when the document fetch fails', async () => {
    const credentials = TestBed.inject(RunnerCredentialsService);
    credentials.setAuthValue('Bearer', 'still-valid-token');

    const fixture = await createComponentWithConfig();
    httpMock
      .expectOne('/openapi/v1.json')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(credentials.authValue('Bearer')).toBe('still-valid-token');
  });

  describe('retrying the OpenAPI document with a remembered bearer value', () => {
    it('shows the error directly on 401 when nothing was ever sent from this panel', async () => {
      const fixture = await createComponentWithConfig();
      httpMock
        .expectOne('/openapi/v1.json')
        .flush('nope', { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();

      // pt-BR is the default language.
      expect(fixture.nativeElement.textContent).toContain(
        'Não foi possível carregar o documento OpenAPI.',
      );
    });

    it('retries once with the remembered value and loads the document', async () => {
      TestBed.inject(RunnerCredentialsService).recordBearerUsage('abcdef82a');
      const fixture = await createComponentWithConfig();
      httpMock
        .expectOne('/openapi/v1.json')
        .flush('nope', { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();

      const retry = httpMock.expectOne('/openapi/v1.json');
      expect(retry.request.headers.get('Authorization')).toBe('Bearer abcdef82a');
      retry.flush(DOC);
      await fixture.whenStable();

      expect(fixture.componentInstance.endpoints()).toHaveLength(1);
      expect(fixture.componentInstance.loadError()).toBeNull();
    });

    it('gives up after one retry, without looping', async () => {
      TestBed.inject(RunnerCredentialsService).recordBearerUsage('abcdef82a');
      const fixture = await createComponentWithConfig();
      httpMock
        .expectOne('/openapi/v1.json')
        .flush('nope', { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();

      httpMock
        .expectOne('/openapi/v1.json')
        .flush('nope', { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();

      httpMock.expectNone('/openapi/v1.json');
      expect(fixture.componentInstance.loadError()).toBe('action.openApiError.title');
    });
  });

  describe('Authorize section', () => {
    it('does not render when the document declares no security scheme', async () => {
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
      await fixture.whenStable();

      const nativeElement: HTMLElement = fixture.nativeElement;
      expect(nativeElement.querySelector('.endpoint-form__authorize')).toBeNull();
    });

    it('renders one field per declared scheme, including oauth2, with name and type in the label', async () => {
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      await fixture.whenStable();

      const nativeElement: HTMLElement = fixture.nativeElement;
      const rows = nativeElement.querySelectorAll('.endpoint-form__authorize-row');
      expect(rows).toHaveLength(3);

      const labelText = nativeElement.textContent ?? '';
      // pt-BR is the default language.
      expect(labelText).toContain('Bearer (http)');
      expect(labelText).toContain('ApiKeyScheme (apiKey)');
      expect(labelText).toContain('OAuth2Scheme (oauth2, cole um access token)');
    });

    it('previews the masked header that will be sent as the value is typed', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      credentials.setAuthValue('Bearer', 'abcdef82a');
      await fixture.whenStable();

      // pt-BR is the default language.
      expect(fixture.nativeElement.textContent).toContain(
        'Enviado como header: Authorization: Bearer ••••f82a',
      );
    });

    it('shows how long a pasted JWT has left, and says so when it already expired', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      credentials.setAuthValue('Bearer', jwtExpiringInSeconds(14 * 60));
      await fixture.whenStable();

      // pt-BR is the default language.
      expect(fixture.nativeElement.textContent).toContain('Expira em 14 min');

      credentials.setAuthValue('Bearer', jwtExpiringInSeconds(-3 * 60));
      await fixture.whenStable();

      expect(fixture.nativeElement.textContent).toContain('Expirado há 3 min');
    });

    it('shows no expiry hint for a token that is not a JWT', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      credentials.setAuthValue('Bearer', 'd7f3a9c1e5b2');
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('.endpoint-form__authorize-expiry')).toBeNull();
    });

    it('keeps warning when the only filled scheme is one the panel cannot apply', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_UNAPPLIABLE_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      await fixture.whenStable();
      expect(fixture.componentInstance.noAlternativeFilled()).toBe(true);

      credentials.setAuthValue('BasicScheme', 'dXNlcjpwYXNz');
      await fixture.whenStable();

      expect(fixture.componentInstance.noAlternativeFilled()).toBe(true);
    });

    it('stops warning once a scheme the panel can actually apply is filled', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      await fixture.whenStable();
      expect(fixture.componentInstance.noAlternativeFilled()).toBe(true);

      credentials.setAuthValue('Bearer', 'abcdef82a');
      await fixture.whenStable();

      expect(fixture.componentInstance.noAlternativeFilled()).toBe(false);
    });
  });

  describe('automatic authorization header', () => {
    it('applies Authorization: Bearer automatically for a required, filled scheme, stripping an already-pasted "Bearer " prefix', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      fixture.componentInstance.setFieldValue('id', '5');
      credentials.setAuthValue('Bearer', 'Bearer xyz');
      fixture.componentInstance.send();
      await fixture.whenStable();

      const req = httpMock.expectOne('/api/orders/5');
      expect(req.request.headers.get('Authorization')).toBe('Bearer xyz');
      req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    });

    it('applies no header when effectiveSecurity is empty, even for an operation explicitly marked security: []', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('DELETE', '/api/orders/{id}');
      fixture.componentInstance.setFieldValue('id', '5');
      credentials.setAuthValue('Bearer', 'xyz');
      fixture.componentInstance.send();
      await fixture.whenStable();

      const req = httpMock.expectOne('/api/orders/5');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    });

    it('lets a manual header with the same name override the automatic one, without duplicating it', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      fixture.componentInstance.setFieldValue('id', '5');
      credentials.setAuthValue('Bearer', 'auto-token');
      fixture.componentInstance.addHeader();
      fixture.componentInstance.setHeaderName(0, 'Authorization');
      fixture.componentInstance.setHeaderValue(0, 'Bearer manual-token');
      fixture.componentInstance.send();
      await fixture.whenStable();

      const req = httpMock.expectOne('/api/orders/5');
      expect(req.request.headers.getAll('Authorization')).toEqual(['Bearer manual-token']);
      req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    });

    it('shows the "no alternative filled" warning only when every OR alternative is empty, and clears once one is filled', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('GET', '/api/orders/{id}');
      await fixture.whenStable();

      // pt-BR is the default language.
      expect(fixture.nativeElement.textContent).toContain(
        'Esta operação exige autenticação e nenhuma das opções acima está preenchida.',
      );

      credentials.setAuthValue('ApiKeyScheme', 'xyz');
      await fixture.whenStable();

      expect(fixture.nativeElement.textContent).not.toContain(
        'Esta operação exige autenticação e nenhuma das opções acima está preenchida.',
      );
    });

    it('shows the mirrored "not declared" note when the operation requires no security but an Authorize field is filled anyway', async () => {
      const credentials = TestBed.inject(RunnerCredentialsService);
      const fixture = await createComponentWithConfig();
      httpMock.expectOne('/openapi/v1.json').flush(DOC_WITH_SECURITY);
      await fixture.whenStable();

      fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
      await fixture.whenStable();
      // Nothing filled in yet: no note.
      expect(fixture.nativeElement.textContent).not.toContain(
        'Esta operação não declara exigir autenticação',
      );

      credentials.setAuthValue('Bearer', 'xyz');
      await fixture.whenStable();

      // pt-BR is the default language.
      expect(fixture.nativeElement.textContent).toContain(
        'Esta operação não declara exigir autenticação; nenhum valor de Autorizar será aplicado aqui.',
      );

      fixture.componentInstance.setFieldValue('id', '5');
      fixture.componentInstance.send();
      await fixture.whenStable();

      const req = httpMock.expectOne('/api/orders/5');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    });
  });
});
