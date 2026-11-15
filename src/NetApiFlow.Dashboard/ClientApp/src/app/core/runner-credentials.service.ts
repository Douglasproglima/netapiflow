import { Injectable, signal } from '@angular/core';

export interface CustomHeader {
  name: string;
  value: string;
}

const AUTH_VALUES_KEY = 'naf-runner-auth-values';
const CUSTOM_HEADERS_KEY = 'naf-runner-custom-headers';

function readStoredJson<T>(key: string, fallback: T): T {
  try {
    const stored = sessionStorage.getItem(key);
    return stored ? (JSON.parse(stored) as T) : fallback;
  } catch {
    return fallback;
  }
}

function writeStoredJson(key: string, value: unknown): void {
  try {
    sessionStorage.setItem(key, JSON.stringify(value));
  } catch {
    // sessionStorage may be unavailable (private browsing, exhausted quota, a blocked iframe);
    // the value still works for the rest of this tab, it just won't survive a refresh.
  }
}

/**
 * Holds the two things a request needs beyond its own fields: the Runner's pasted authorization
 * value per security scheme name, and the manually added headers. Both write through to
 * `sessionStorage` so neither is lost on a refresh or on picking a different endpoint, unlike the
 * `EndpointFormComponent`-local state this replaces. Keyed by scheme name, not type: two `http`
 * bearer schemes with different names (e.g. `BearerUser`/`BearerAdmin`) are distinct fields.
 */
@Injectable({ providedIn: 'root' })
export class RunnerCredentialsService {
  private readonly authValuesState = signal<Record<string, string>>(
    readStoredJson(AUTH_VALUES_KEY, {}),
  );
  private readonly customHeadersState = signal<CustomHeader[]>(
    readStoredJson(CUSTOM_HEADERS_KEY, []),
  );

  readonly customHeaders = this.customHeadersState.asReadonly();

  authValue(schemeName: string): string {
    return this.authValuesState()[schemeName] ?? '';
  }

  setAuthValue(schemeName: string, value: string): void {
    this.updateAuthValues((values) => ({ ...values, [schemeName]: value }));
  }

  addHeader(): void {
    this.updateCustomHeaders((headers) => [...headers, { name: '', value: '' }]);
  }

  removeHeader(index: number): void {
    this.updateCustomHeaders((headers) => headers.filter((_, i) => i !== index));
  }

  setHeaderName(index: number, name: string): void {
    this.updateCustomHeaders((headers) =>
      headers.map((header, i) => (i === index ? { ...header, name } : header)),
    );
  }

  setHeaderValue(index: number, value: string): void {
    this.updateCustomHeaders((headers) =>
      headers.map((header, i) => (i === index ? { ...header, value } : header)),
    );
  }

  /**
   * Drops any saved auth value whose scheme name is no longer in the reloaded OpenAPI document,
   * so a stale value never keeps reading as "filled in" for a scheme that no longer exists. Call
   * only after a successful document fetch: a failed reload has no new scheme list to reconcile
   * against, and must leave every saved value untouched.
   */
  reconcile(currentSchemeNames: readonly string[]): void {
    const validNames = new Set(currentSchemeNames);
    this.updateAuthValues((values) =>
      Object.fromEntries(Object.entries(values).filter(([name]) => validNames.has(name))),
    );
  }

  private updateAuthValues(
    updater: (values: Record<string, string>) => Record<string, string>,
  ): void {
    this.authValuesState.update(updater);
    writeStoredJson(AUTH_VALUES_KEY, this.authValuesState());
  }

  private updateCustomHeaders(updater: (headers: CustomHeader[]) => CustomHeader[]): void {
    this.customHeadersState.update(updater);
    writeStoredJson(CUSTOM_HEADERS_KEY, this.customHeadersState());
  }
}
