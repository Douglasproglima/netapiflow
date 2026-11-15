import { Observable, catchError, map, of } from 'rxjs';

export type ObservableResult<T> = { readonly ok: true; readonly value: T } | { readonly ok: false };

/**
 * `switchMap`-friendly wrapper: an inner observable that errors terminates the whole outer
 * subscription (no further emissions ever, even from a healthy source), so every polling
 * stream in this app routes its HTTP call through this instead of letting the error reach
 * `switchMap` itself.
 */
export function catchAsResult<T>(source: Observable<T>): Observable<ObservableResult<T>> {
  return source.pipe(
    map((value): ObservableResult<T> => ({ ok: true, value })),
    catchError(() => of<ObservableResult<T>>({ ok: false })),
  );
}
