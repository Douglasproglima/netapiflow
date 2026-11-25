import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { EndpointFormComponent } from './endpoint-form.component';
import { LanguageService } from '../../core/i18n/language.service';
import { DashboardConfig, OpenApiDocument } from '../../core/models';

const CONFIG: DashboardConfig = {
  slowThresholdMicros: 250_000,
  version: '0.1.0',
  tailSamplingEnabled: false,
  successSampleRate: 0.1,
  openApiDocumentPath: '/openapi/v1.json',
  hostPathBase: '',
};

const DOC: OpenApiDocument = {
  paths: {
    '/api/orders/{id}': {
      get: {
        operationId: 'GetOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
      },
    },
    '/api/ping': { get: { operationId: 'Ping' } },
    '/api/orders': {
      post: {
        operationId: 'CreateOrder',
        requestBody: {
          required: true,
          content: { 'application/json': { example: { customerId: 7, items: ['a', 'b'] } } },
        },
      },
    },
    '/api/refunds': {
      post: {
        operationId: 'CreateRefund',
        requestBody: { required: true, content: { 'application/json': { schema: {} } } },
      },
    },
  },
};

const TRACE_ID = '0123456789abcdef0123456789abcdef';

describe('what the Runner shows of the response', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<EndpointFormComponent>;
  let root: HTMLElement;

  beforeEach(async () => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(LanguageService).setLanguage('en');
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EndpointFormComponent);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush(CONFIG);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
    localStorage.clear();
  });

  async function pick(key: string): Promise<void> {
    root.querySelector<HTMLButtonElement>(`[title="${key}"]`)!.click();
    await fixture.whenStable();
  }

  async function send(): Promise<void> {
    root.querySelector<HTMLButtonElement>('.endpoint-form__send')!.click();
    await fixture.whenStable();
  }

  async function answer(
    url: string,
    body: string | null,
    options: { status?: number; contentType?: string; headers?: Record<string, string> } = {},
  ): Promise<void> {
    const status = options.status ?? 200;
    httpMock.expectOne(url).flush(body, {
      status,
      statusText: status === 200 ? 'OK' : 'Failure',
      headers: {
        'X-NetApiFlow-TraceId': TRACE_ID,
        ...(options.contentType ? { 'Content-Type': options.contentType } : {}),
        ...options.headers,
      },
    });
    await fixture.whenStable();
  }

  function block(): HTMLElement | null {
    return root.querySelector('.endpoint-form__response');
  }

  function text(selector: string): string | undefined {
    return root.querySelector(selector)?.textContent?.trim();
  }

  it('shows the status and the body of a successful answer, with the body folded away', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong', { contentType: 'text/plain' });

    expect(text('.endpoint-form__response-status')).toBe('HTTP 200');
    expect(root.querySelector('details.endpoint-form__response-body')!.hasAttribute('open')).toBe(
      false,
    );
    expect(text('.endpoint-form__response-text')).toBe('pong');
    expect(text('.endpoint-form__response-body summary')).toBe('Response body');
  });

  it.each([
    [404, 'not found'],
    [500, 'boom'],
  ])('shows the status of a %s answer and its body', async (status, body) => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', body, { status, contentType: 'text/plain' });

    expect(text('.endpoint-form__response-status')).toBe(`HTTP ${status}`);
    expect(text('.endpoint-form__response-text')).toBe(body);
  });

  it('shows no status when the request got no answer at all, and keeps the message it already had', async () => {
    await pick('GET /api/ping');
    await send();
    httpMock.expectOne('/api/ping').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(block()).toBeNull();
    expect(root.textContent).not.toContain('HTTP 0');
    expect(root.querySelector('.endpoint-form__status--error')).not.toBeNull();
  });

  it('turns the trace id into a link of the app that opens that trace', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong', { contentType: 'text/plain' });

    const link = root.querySelector<HTMLAnchorElement>('.endpoint-form__trace-id a')!;
    expect(link.getAttribute('href')).toBe(`/?trace=${TRACE_ID}`);
    expect(link.textContent).toContain(TRACE_ID);
  });

  it('shows the status and the body and does not navigate away by itself', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong', { contentType: 'text/plain' });

    expect(location.pathname).not.toContain('trace');
    expect(location.search).not.toContain(TRACE_ID);
  });

  it('never shows a header of the answer, only its status and body', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong', {
      contentType: 'text/plain',
      headers: { 'Set-Cookie': 'session=top-secret', 'X-Token': 'bearer-secret' },
    });

    expect(root.textContent).not.toContain('top-secret');
    expect(root.textContent).not.toContain('bearer-secret');
    expect(root.textContent).not.toContain('Set-Cookie');
  });

  it('shows markup in the body as text and never as markup', async () => {
    const hostile = '<img src=x onerror=alert(1)><script>alert(2)</script>';
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', hostile, { contentType: 'text/html' });

    expect(text('.endpoint-form__response-text')).toBe(hostile);
    expect(
      root.querySelector('.endpoint-form__response img, .endpoint-form__response script'),
    ).toBeNull();
  });

  it('keeps only the first 4000 characters of a long body and says it cut the rest', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'a'.repeat(5_000_000), { contentType: 'text/plain' });

    expect(root.querySelector('.endpoint-form__response-text')!.textContent).toHaveLength(4000);
    expect(text('.endpoint-form__response-note')).toBe('Cut at 4000 characters.');
  });

  it('does not say it cut anything when the body fits', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'a'.repeat(4000), { contentType: 'text/plain' });

    expect(root.querySelector('.endpoint-form__response-text')!.textContent).toHaveLength(4000);
    expect(root.querySelector('.endpoint-form__response-note')).toBeNull();
  });

  it('says so when the body is empty', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', '', { contentType: 'text/plain' });

    expect(text('.endpoint-form__response-text')).toBe('(empty body)');
  });

  it.each(['application/json', 'application/problem+json', 'application/xml', 'text/csv'])(
    'shows a body of type %s',
    async (contentType) => {
      await pick('GET /api/ping');
      await send();
      await answer('/api/ping', 'content', { contentType });

      expect(text('.endpoint-form__response-text')).toBe('content');
    },
  );

  it('does not show a body that is not text, and says its type', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', '\u0000PNG', { contentType: 'application/octet-stream' });

    expect(text('.endpoint-form__response-text')).toBe(
      '(content application/octet-stream, not shown)',
    );
  });

  it('shows the body when the answer says nothing about its type', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong');

    expect(text('.endpoint-form__response-text')).toBe('pong');
  });

  it('keeps the body out of every storage and out of the address of the page', async () => {
    const secret = 'body-that-must-stay-in-memory-4f9a';
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', secret, { contentType: 'text/plain' });

    const stored = [localStorage, sessionStorage].map((storage) =>
      JSON.stringify(Object.fromEntries(Object.entries(storage))),
    );
    expect(stored.join('')).not.toContain(secret);
    expect(location.href).not.toContain(secret);
  });

  it('drops the body of the last send as soon as the next send starts', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'first', { contentType: 'text/plain' });
    expect(text('.endpoint-form__response-text')).toBe('first');

    await send();
    expect(block()).toBeNull();

    await answer('/api/ping', 'second', { contentType: 'text/plain' });
    expect(text('.endpoint-form__response-text')).toBe('second');
  });

  it('drops the body when another endpoint is picked', async () => {
    await pick('GET /api/ping');
    await send();
    await answer('/api/ping', 'pong', { contentType: 'text/plain' });

    root.querySelector<HTMLButtonElement>('.endpoint-form__back')!.click();
    await fixture.whenStable();
    await pick('GET /api/orders/{id}');

    expect(block()).toBeNull();
  });

  it('puts a space between the name of a field and what kind of field it is', async () => {
    await pick('GET /api/orders/{id}');

    const label = root.querySelector('.endpoint-form__field label')!;
    expect(label.textContent).toMatch(/^id\s+\S/);
  });

  describe('the body of a POST', () => {
    it('starts with the example the OpenAPI document gives for the request body', async () => {
      await pick('POST /api/orders');

      const textarea = root.querySelector<HTMLTextAreaElement>('textarea')!;
      expect(JSON.parse(textarea.value)).toEqual({ customerId: 7, items: ['a', 'b'] });
    });

    it('starts empty when the document gives no example', async () => {
      await pick('POST /api/refunds');

      expect(root.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('');
    });
  });
});
