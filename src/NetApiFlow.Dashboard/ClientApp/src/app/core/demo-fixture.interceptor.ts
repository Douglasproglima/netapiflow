import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpHeaders,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, map, of, shareReplay, switchMap, throwError } from 'rxjs';

export const DEMO_FIXTURES_PATH = 'assets/demo-fixtures';
export const DEMO_MANIFEST_PATH = `${DEMO_FIXTURES_PATH}/manifest.json`;
export const TRACE_ID_HEADER = 'X-NetApiFlow-TraceId';

export interface DemoSampleResponse {
  method: string;
  template: string;
  status: number;
  contentType: string;
  body: string | null;
  traceId: string;
}

export interface DemoManifest {
  routes: Record<string, string>;
  noContent?: string[];
  fallbacks?: Record<string, string>;
  documents?: Record<string, string>;
  sampleResponses?: DemoSampleResponse[];
}

export function demoFixtureInterceptor(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> {
  if (isFixtureRequest(request)) {
    return next(request);
  }

  const http = inject(HttpClient);

  return loadManifest(http).pipe(
    switchMap((manifest) => {
      if (isRecordedAsNoContent(manifest, request)) {
        return of(new HttpResponse<null>({ body: null, status: 204, url: request.urlWithParams }));
      }

      const file = resolveFixtureFile(manifest, request);
      if (file) {
        return http
          .get<unknown>(`${DEMO_FIXTURES_PATH}/${file}`)
          .pipe(map((body) => new HttpResponse({ body, status: 200, url: request.urlWithParams })));
      }

      const sampleResponse = isDashboardApiRequest(request)
        ? undefined
        : findSampleResponse(manifest, request.method, request.url);
      if (sampleResponse) {
        return answerFromRecording(http, sampleResponse, request);
      }

      return isDashboardApiRequest(request) ? throwError(() => notFound(request)) : next(request);
    }),
  );
}

let manifest$: Observable<DemoManifest> | null = null;

function loadManifest(http: HttpClient): Observable<DemoManifest> {
  manifest$ ??= http.get<DemoManifest>(DEMO_MANIFEST_PATH).pipe(
    catchError(() => of({ routes: {} } satisfies DemoManifest)),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  return manifest$;
}

export function resetDemoManifestCache(): void {
  manifest$ = null;
}

function isFixtureRequest(request: HttpRequest<unknown>): boolean {
  return request.url.startsWith(DEMO_FIXTURES_PATH);
}

function isDashboardApiRequest(request: HttpRequest<unknown>): boolean {
  return request.url.startsWith('api/');
}

function notFound(request: HttpRequest<unknown>): HttpErrorResponse {
  return new HttpErrorResponse({
    status: 404,
    statusText: 'Not Found',
    url: request.urlWithParams,
  });
}

function answerFromRecording(
  http: HttpClient,
  recording: DemoSampleResponse,
  request: HttpRequest<unknown>,
): Observable<HttpEvent<unknown>> {
  const headers = new HttpHeaders()
    .set(TRACE_ID_HEADER, recording.traceId)
    .set('Content-Type', recording.contentType);
  const body$ = recording.body ? readRecordedBody(http, recording) : of(null);

  return body$.pipe(
    map(
      (body) =>
        new HttpResponse({ body, status: recording.status, headers, url: request.urlWithParams }),
    ),
  );
}

function readRecordedBody(http: HttpClient, recording: DemoSampleResponse): Observable<unknown> {
  const file = `${DEMO_FIXTURES_PATH}/${recording.body}`;
  return recording.contentType.includes('json')
    ? http.get<unknown>(file)
    : http.get(file, { responseType: 'text' });
}

export function findSampleResponse(
  manifest: DemoManifest,
  method: string,
  url: string,
): DemoSampleResponse | undefined {
  const requestedSegments = pathSegments(url);

  return (manifest.sampleResponses ?? [])
    .filter(
      (recording) =>
        recording.method === method.toUpperCase() &&
        matchesTemplate(pathSegments(recording.template), requestedSegments),
    )
    .sort((left, right) => literalSegmentCount(right.template) - literalSegmentCount(left.template))
    .at(0);
}

function pathSegments(urlOrTemplate: string): string[] {
  return urlOrTemplate
    .replace(/^https?:\/\/[^/]+/, '')
    .split('?')[0]
    .split('/')
    .filter((segment) => segment.length > 0);
}

function matchesTemplate(templateSegments: string[], requestedSegments: string[]): boolean {
  return (
    templateSegments.length === requestedSegments.length &&
    templateSegments.every(
      (segment, index) =>
        isParameter(segment) || segment.toLowerCase() === requestedSegments[index].toLowerCase(),
    )
  );
}

function isParameter(segment: string): boolean {
  return segment.startsWith('{') && segment.endsWith('}');
}

function literalSegmentCount(template: string): number {
  return pathSegments(template).filter((segment) => !isParameter(segment)).length;
}

function isRecordedAsNoContent(manifest: DemoManifest, request: HttpRequest<unknown>): boolean {
  return (
    isDashboardApiRequest(request) &&
    (manifest.noContent ?? []).includes(normalizeKey(request.urlWithParams))
  );
}

export function resolveFixtureFile(
  manifest: DemoManifest,
  request: HttpRequest<unknown>,
): string | undefined {
  const document = manifest.documents?.[request.url];
  if (document) {
    return document;
  }

  if (!isDashboardApiRequest(request)) {
    return undefined;
  }

  const withParams = normalizeKey(request.urlWithParams);
  const withoutParams = normalizeKey(request.url);

  return (
    manifest.routes[withParams] ??
    manifest.routes[withoutParams] ??
    findFallback(manifest, withoutParams)
  );
}

function findFallback(manifest: DemoManifest, path: string): string | undefined {
  let candidate = path;
  while (candidate.includes('/')) {
    candidate = candidate.slice(0, candidate.lastIndexOf('/'));
    const file = manifest.fallbacks?.[candidate];
    if (file) {
      return file;
    }
  }

  return undefined;
}

function normalizeKey(url: string): string {
  const withoutBase = url.replace(/^.*?\bapi\//, 'api/').replace(/^\/+/, '');
  const [path, query] = withoutBase.split('?');
  if (!query) {
    return path;
  }

  const canonical = [...new URLSearchParams(query).entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([name, value]) => `${name}=${value}`)
    .join('&');

  return `${path}?${canonical}`;
}
