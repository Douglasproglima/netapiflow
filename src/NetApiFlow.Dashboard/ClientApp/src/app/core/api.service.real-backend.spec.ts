import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';

const TRACE_ID_HEADER = 'X-NetApiFlow-TraceId';

interface RecordedAnswer {
  status: number;
  body: string | null;
  headers?: Record<string, string>;
}

async function dispatchAgainstTheRealBackend(answer: RecordedAnswer) {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(answer.body, { status: answer.status, headers: answer.headers }),
      ),
    ),
  );
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  return firstValueFrom(TestBed.inject(ApiService).callEndpoint('GET', '/api/orders/5'));
}

describe('ApiService callEndpoint against the real HTTP backend', () => {
  afterEach(() => vi.unstubAllGlobals());

  const successfulAnswers: [string, RecordedAnswer][] = [
    ['a JSON body', { status: 200, body: '{"id":5}', headers: { [TRACE_ID_HEADER]: 't1' } }],
    ['a plain-text body', { status: 200, body: 'untraced' }],
    ['an HTML body', { status: 200, body: '<p>ok</p>' }],
    ['an empty body', { status: 200, body: '' }],
    ['a 204 with no body', { status: 204, body: null }],
  ];

  it.each(successfulAnswers)('takes %s for a success', async (_name, answer) => {
    const response = await dispatchAgainstTheRealBackend(answer);

    expect(response.status).toBe(answer.status);
  });

  it('keeps the trace id header of a plain-text success readable', async () => {
    const response = await dispatchAgainstTheRealBackend({
      status: 200,
      body: 'ok',
      headers: { [TRACE_ID_HEADER]: 't7' },
    });

    expect(response.headers.get(TRACE_ID_HEADER)).toBe('t7');
  });

  it('still reports a real failure as a failure, with its status and header', async () => {
    const failure = await dispatchAgainstTheRealBackend({
      status: 500,
      body: 'Internal Server Error',
      headers: { [TRACE_ID_HEADER]: 't9' },
    }).catch((error: { status: number; headers: { get(name: string): string | null } }) => error);

    expect(failure).toMatchObject({ status: 500 });
    expect(
      (failure as { headers: { get(name: string): string | null } }).headers.get(TRACE_ID_HEADER),
    ).toBe('t9');
  });
});
