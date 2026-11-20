import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, shareReplay, switchMap, throwError } from 'rxjs';

export const DEMO_FIXTURES_PATH = 'assets/demo-fixtures';
export const DEMO_MANIFEST_PATH = `${DEMO_FIXTURES_PATH}/manifest.json`;

/**
 * Maps a request the dashboard would send to the file holding a recorded answer for it. The key
 * is the request as the dashboard writes it (`api/traces?status=Error`), so a fixture is matched
 * without re-deriving route shapes here.
 */
export interface DemoManifest {
  routes: Record<string, string>;
  fallbacks?: Record<string, string>;

  /**
   * Paths outside the dashboard's own API that a screen still fetches, keyed exactly as
   * requested. The host's OpenAPI document is the one that matters: the Runner reads it, and
   * without it the demo would log a 404 on every load.
   */
  documents?: Record<string, string>;
}

/**
 * Answers the dashboard's own REST calls from recorded files instead of the network, for the
 * static demo build. Nothing in `ApiService` changes: a fixture holds exactly the payload the
 * real endpoint returns, so the screens run against the same contract.
 */
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
      const file = resolveFixtureFile(manifest, request);
      if (!file) {
        // Nothing recorded for it: a dashboard API call has nowhere else to go, anything else
        // (a static asset served alongside the page) is still a real file on disk.
        return isDashboardApiRequest(request)
          ? throwError(
              () =>
                new HttpErrorResponse({
                  status: 404,
                  statusText: 'Not Found',
                  url: request.urlWithParams,
                }),
            )
          : next(request);
      }

      return http
        .get<unknown>(`${DEMO_FIXTURES_PATH}/${file}`)
        .pipe(map((body) => new HttpResponse({ body, status: 200, url: request.urlWithParams })));
    }),
  );
}

let manifest$: Observable<DemoManifest> | null = null;

/**
 * The in-flight request is cached, not just its result: the dashboard fires several API calls at
 * once on load, and caching only the resolved value would send one manifest request per call.
 */
function loadManifest(http: HttpClient): Observable<DemoManifest> {
  manifest$ ??= http.get<DemoManifest>(DEMO_MANIFEST_PATH).pipe(
    catchError(() => of({ routes: {} } satisfies DemoManifest)),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  return manifest$;
}

/** Exposed so a test can start from a known state; the manifest never changes at run time. */
export function resetDemoManifestCache(): void {
  manifest$ = null;
}

/** The interceptor's own reads, which must never be intercepted again. */
function isFixtureRequest(request: HttpRequest<unknown>): boolean {
  return request.url.startsWith(DEMO_FIXTURES_PATH);
}

function isDashboardApiRequest(request: HttpRequest<unknown>): boolean {
  return request.url.startsWith('api/') || request.url.includes('/api/');
}

/**
 * Prefers an exact match including the query string, so `/traces?status=Error` can have its own
 * recorded answer, and falls back to the path alone, which is what every filter combination the
 * recording did not capture lands on.
 */
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

/**
 * A fallback is keyed by the collection (`api/traces`), so any id the recording never captured
 * still resolves to something. Shows a recorded trace instead of a 404 in a build where the
 * reader cannot produce new data.
 */
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

/**
 * Canonical form of a request: the path, then its query decoded and sorted by name. The dashboard
 * builds query strings with `HttpParams`, whose escaping differs from the recorder's, so matching
 * on the raw URL would miss. Decoding both sides removes the question entirely.
 */
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
