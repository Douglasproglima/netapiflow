import { DOCUMENT } from '@angular/common';
import { Component, computed, inject, output, signal } from '@angular/core';
import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { OpenApiDocument, OpenApiSecurityScheme } from '../../core/models';
import { RunnerCredentialsService } from '../../core/runner-credentials.service';
import { httpMethodTagClass } from '../../shared/http-method-style';
import {
  AuthApplication,
  buildAuthApplication,
  decodeJwtExpiry,
  isTokenBearingScheme,
  buildEndpointForm,
  effectiveSecurity,
  FormFieldKind,
  hasSecurityDeclaration,
  listOpenApiEndpoints,
  listSecuritySchemes,
  OpenApiEndpoint,
  partitionDashboardRoutes,
  previewAuthApplication,
  resolveAgainstHostRoot,
  SecuritySchemeEntry,
} from './openapi-form';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationKey } from '../../core/i18n/translations';

/**
 * Lets the user pick an endpoint from the host application's own OpenAPI document, fill in its
 * path/query parameters and JSON body, and dispatch it. Shared by the `/runner` route and the
 * `/flow` workspace's action panel: it only emits the dispatched trace id, leaving what to do with
 * it (track it inline, navigate to it) to whichever page embeds it. Where that document lives is
 * the host's choice, so it comes from `GET /config` (`NetApiFlowOptions.OpenApiDocumentPath`); the
 * path is root-relative, since the host's endpoints live at the application root whatever path the
 * dashboard itself is mounted on (unlike `ApiService`'s own relative URLs). The dashboard's own
 * `{basePath}/api/*` routes are read from the page's own `<base href>` and hidden by default,
 * since the host's OpenAPI document picks them up alongside the user's real endpoints.
 */
@Component({
  selector: 'app-endpoint-form',
  templateUrl: './endpoint-form.component.html',
  styleUrl: './endpoint-form.component.scss',
  imports: [MatButtonModule, TranslatePipe],
})
export class EndpointFormComponent {
  private readonly api = inject(ApiService);
  private readonly config = inject(ConfigService);
  private readonly document = inject(DOCUMENT);
  protected readonly credentials = inject(RunnerCredentialsService);

  readonly traceDispatched = output<string>();

  readonly loading = signal(true);
  readonly loadError = signal<TranslationKey | null>(null);
  private readonly openApiDocument = signal<OpenApiDocument | null>(null);

  readonly routeFilter = signal('');
  private readonly dashboardBasePath = new URL(this.document.baseURI).pathname.replace(/\/$/, '');

  private readonly endpointsPartition = computed(() => {
    const doc = this.openApiDocument();
    const endpoints = doc ? listOpenApiEndpoints(doc) : [];
    return partitionDashboardRoutes(endpoints, this.dashboardBasePath);
  });

  readonly hiddenDashboardRouteCount = computed(() => this.endpointsPartition().hiddenCount);
  readonly hiddenDashboardRoutePrefix = `${this.dashboardBasePath}/`;

  hiddenRoutesNoteKey(): TranslationKey {
    return this.hiddenDashboardRouteCount() === 1
      ? 'action.hiddenRoutesNote.one'
      : 'action.hiddenRoutesNote.many';
  }

  fieldKindKey(kind: FormFieldKind): TranslationKey {
    return `action.field.kind.${kind}`;
  }

  readonly endpoints = computed(() => {
    const filter = this.routeFilter().trim().toLowerCase();
    const visible = this.endpointsPartition().visible;
    return filter
      ? visible.filter((endpoint) => endpoint.path.toLowerCase().includes(filter))
      : visible;
  });

  /**
   * Deliberately not `hasSecurityDeclaration`, which is true for an operation declaring
   * `security: []` as well. OpenAPI defines that empty array as "explicitly public", so a lock
   * there would claim the opposite of what the document says.
   */
  requiresAuth(endpoint: OpenApiEndpoint): boolean {
    const doc = this.openApiDocument();
    return !!doc && effectiveSecurity(doc, endpoint.method, endpoint.path).length > 0;
  }

  readonly anyEndpointRequiresAuth = computed(() =>
    this.endpointsPartition().visible.some((endpoint) => this.requiresAuth(endpoint)),
  );

  readonly selected = signal<{ method: string; path: string } | null>(null);
  readonly form = computed(() => {
    const doc = this.openApiDocument();
    const selected = this.selected();
    return doc && selected ? buildEndpointForm(doc, selected.method, selected.path) : null;
  });

  private readonly securitySchemes = computed(() => {
    const doc = this.openApiDocument();
    return doc ? listSecuritySchemes(doc) : [];
  });

  /** `apiKey in: cookie` is out of reach for this panel: a real session cookie already rides
   *  along natively on a same-origin request, and `fetch` cannot set `Cookie` manually. */
  readonly authorizeSchemes = computed(() =>
    this.securitySchemes().filter(
      (entry) => !(entry.scheme.type === 'apiKey' && entry.scheme.in === 'cookie'),
    ),
  );

  private readonly schemesByName = computed(
    () => new Map(this.securitySchemes().map((entry) => [entry.name, entry.scheme])),
  );

  private readonly securityAlternatives = computed(() => {
    const doc = this.openApiDocument();
    const selected = this.selected();
    return doc && selected ? effectiveSecurity(doc, selected.method, selected.path) : [];
  });

  private readonly operationDeclaresSecurity = computed(() => {
    const doc = this.openApiDocument();
    const selected = this.selected();
    return !!doc && !!selected && hasSecurityDeclaration(doc, selected.method, selected.path);
  });

  /**
   * `effectiveSecurity` is a list of OR alternatives (each itself an AND of scheme names), so a
   * route with two alternatives where only one is filled in is already fully authorized: the
   * warning only fires when every alternative still has at least one scheme this panel cannot
   * apply. A filled-in value is not enough on its own, since a scheme the panel has no way to
   * send (`http`/`basic`, `apiKey in: cookie`) would silence the warning while the request still
   * leaves without any authorization at all.
   */
  readonly noAlternativeFilled = computed(() => {
    const alternatives = this.securityAlternatives();
    if (alternatives.length === 0) {
      return false;
    }
    return !alternatives.some((alternative) =>
      Object.keys(alternative).every((name) => this.isApplied(name)),
    );
  });

  private isApplied(schemeName: string): boolean {
    const scheme = this.schemesByName().get(schemeName);
    return (
      !!scheme && buildAuthApplication(scheme, this.credentials.authValue(schemeName)) !== null
    );
  }

  /** Mirror of the warning above: nothing declared here, yet the user filled a value anyway. */
  readonly noSecurityDeclaredButFilled = computed(() => {
    if (this.operationDeclaresSecurity()) {
      return false;
    }
    return this.authorizeSchemes().some(
      (entry) => this.credentials.authValue(entry.name).trim() !== '',
    );
  });

  private readonly revealedSchemeNames = signal<ReadonlySet<string>>(new Set());

  readonly fieldValues = signal<Record<string, string>>({});
  readonly sending = signal(false);
  readonly sendError = signal<TranslationKey | null>(null);
  readonly invalidFieldName = signal<string | null>(null);
  readonly lastTraceId = signal<string | null>(null);

  readonly missingTrace = signal<{ key: TranslationKey; status: number } | null>(null);

  private readonly hostPathBase = signal('');

  constructor() {
    this.config.config$.subscribe((config) => {
      this.hostPathBase.set(config.hostPathBase ?? '');
      this.loadOpenApiDocument(
        resolveAgainstHostRoot(this.hostPathBase(), config.openApiDocumentPath),
      );
    });
  }

  /**
   * A host that keeps its own OpenAPI document behind the same authorization as its endpoints
   * answers 401 here, and the Runner has nothing to show. When a bearer value was already sent
   * from this panel, one retry carries it; anything else, including a second failure, falls
   * through to the error state.
   */
  private loadOpenApiDocument(path: string, bearerValue: string | null = null): void {
    const headers = bearerValue
      ? new HttpHeaders({ Authorization: `Bearer ${bearerValue}` })
      : undefined;

    this.api.loadOpenApiDocument(path, headers).subscribe({
      next: (doc) => {
        this.openApiDocument.set(doc);
        this.loading.set(false);
        this.credentials.reconcile(listSecuritySchemes(doc).map((entry) => entry.name));
      },
      error: (error: HttpErrorResponse) => {
        const storedBearer = this.credentials.lastBearerValue();
        if (error.status === 401 && bearerValue === null && storedBearer) {
          this.loadOpenApiDocument(path, storedBearer);
          return;
        }

        this.loadError.set('action.openApiError.title');
        this.loading.set(false);
      },
    });
  }

  setRouteFilter(value: string): void {
    this.routeFilter.set(value);
  }

  selectEndpoint(method: string, path: string): void {
    this.selected.set({ method, path });
    this.fieldValues.set({});
    this.sendError.set(null);
    this.invalidFieldName.set(null);
    this.lastTraceId.set(null);
    this.missingTrace.set(null);
  }

  /**
   * The list and the selected endpoint's form share one scroll area: with a real route count
   * (248 routes) the list alone can run past a thousand pixels, so showing both at once would
   * bury the "Send request" button under the entire list first. Swapping the list out for the
   * form instead keeps the button reachable regardless of how many routes exist.
   */
  backToList(): void {
    this.selected.set(null);
  }

  fieldValue(name: string): string {
    return this.fieldValues()[name] ?? '';
  }

  setFieldValue(name: string, value: string): void {
    this.fieldValues.update((values) => ({ ...values, [name]: value }));
    if (this.invalidFieldName() === name) {
      this.invalidFieldName.set(null);
    }
  }

  onFieldInput(name: string, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLTextAreaElement;
    this.setFieldValue(name, target.value);
  }

  credentialValue(schemeName: string): string {
    return this.credentials.authValue(schemeName);
  }

  onAuthValueInput(schemeName: string, event: Event): void {
    this.credentials.setAuthValue(schemeName, (event.target as HTMLInputElement).value);
  }

  isRevealed(schemeName: string): boolean {
    return this.revealedSchemeNames().has(schemeName);
  }

  toggleReveal(schemeName: string): void {
    this.revealedSchemeNames.update((names) => {
      const next = new Set(names);
      if (next.has(schemeName)) {
        next.delete(schemeName);
      } else {
        next.add(schemeName);
      }
      return next;
    });
  }

  schemeLabelKey(scheme: OpenApiSecurityScheme): TranslationKey {
    return scheme.type === 'oauth2' || scheme.type === 'openIdConnect'
      ? 'action.authorize.schemeLabel.token'
      : 'action.authorize.schemeLabel.plain';
  }

  authPreview(entry: SecuritySchemeEntry): AuthApplication | null {
    return previewAuthApplication(entry.scheme, this.credentials.authValue(entry.name));
  }

  authPreviewKey(preview: AuthApplication): TranslationKey {
    return preview.target === 'query'
      ? 'action.authorize.preview.query'
      : 'action.authorize.preview.header';
  }

  isQueryScheme(scheme: OpenApiSecurityScheme): boolean {
    return scheme.type === 'apiKey' && scheme.in === 'query';
  }

  /**
   * How long the pasted token has left, for a scheme that carries one. Presentation only: an
   * expired token never blocks the request, the server is the one that rejects it.
   */
  authExpiry(entry: SecuritySchemeEntry): { key: TranslationKey; minutes: number } | null {
    if (!isTokenBearingScheme(entry.scheme)) {
      return null;
    }

    const expiry = decodeJwtExpiry(this.credentials.authValue(entry.name));
    if (!expiry) {
      return null;
    }

    const minutes = Math.round((expiry.getTime() - Date.now()) / 60_000);
    return minutes >= 0
      ? { key: 'action.authorize.expiresIn', minutes }
      : { key: 'action.authorize.expired', minutes: -minutes };
  }

  addHeader(): void {
    this.credentials.addHeader();
  }

  removeHeader(index: number): void {
    this.credentials.removeHeader(index);
  }

  setHeaderName(index: number, name: string): void {
    this.credentials.setHeaderName(index, name);
  }

  setHeaderValue(index: number, value: string): void {
    this.credentials.setHeaderValue(index, value);
  }

  onHeaderNameInput(index: number, event: Event): void {
    this.setHeaderName(index, (event.target as HTMLInputElement).value);
  }

  onHeaderValueInput(index: number, event: Event): void {
    this.setHeaderValue(index, (event.target as HTMLInputElement).value);
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  send(): void {
    const form = this.form();
    if (!form) {
      return;
    }

    const emptyPathField = form.fields.find(
      (field) => field.kind === 'path' && this.fieldValue(field.name).trim() === '',
    );
    if (emptyPathField) {
      this.invalidFieldName.set(emptyPathField.name);
      this.sendError.set('action.emptyPathParam');
      return;
    }

    let path = form.path;
    const queryParts: string[] = [];
    let body: unknown;

    for (const field of form.fields) {
      const raw = this.fieldValue(field.name);
      if (field.kind === 'path') {
        path = path.replace(`{${field.name}}`, encodeURIComponent(raw));
      } else if (field.kind === 'query' && raw !== '') {
        queryParts.push(`${encodeURIComponent(field.name)}=${encodeURIComponent(raw)}`);
      } else if (field.kind === 'body') {
        try {
          body = raw ? JSON.parse(raw) : undefined;
        } catch {
          this.sendError.set('action.invalidJson');
          return;
        }
      }
    }

    let headers = new HttpHeaders();
    const requiredSchemeNames = new Set(
      this.securityAlternatives().flatMap((alternative) => Object.keys(alternative)),
    );
    for (const schemeName of requiredSchemeNames) {
      const scheme = this.schemesByName().get(schemeName);
      if (!scheme) {
        continue;
      }
      const application = buildAuthApplication(scheme, this.credentials.authValue(schemeName));
      if (!application) {
        continue;
      }
      if (application.target === 'query') {
        queryParts.push(
          `${encodeURIComponent(application.name)}=${encodeURIComponent(application.value)}`,
        );
      } else {
        headers = headers.set(application.name, application.value);
        if (application.name === 'Authorization') {
          this.credentials.recordBearerUsage(application.value.replace(/^Bearer\s+/i, ''));
        }
      }
    }

    const hostPath = resolveAgainstHostRoot(this.hostPathBase(), path);
    const fullPath = queryParts.length > 0 ? `${hostPath}?${queryParts.join('&')}` : hostPath;

    // A manual header always wins over the automatic one: .set() replaces rather than adds, so
    // the same header name never goes out twice.
    for (const header of this.credentials.customHeaders()) {
      const name = header.name.trim();
      if (name !== '') {
        headers = headers.set(name, header.value);
      }
    }

    this.dispatch(this.api.callEndpoint(form.method, fullPath, body, headers));
  }

  private dispatch(request: Observable<HttpResponse<string>>): void {
    this.sending.set(true);
    this.sendError.set(null);
    this.missingTrace.set(null);
    request.subscribe({
      next: (response) =>
        this.onDispatched(response.headers.get('X-NetApiFlow-TraceId'), response.status),
      error: (error: HttpErrorResponse) => {
        const traceIdOfFailedRequest = error.headers.get('X-NetApiFlow-TraceId');
        if (!traceIdOfFailedRequest) {
          this.sendError.set(this.messageForFailureWithoutTrace());
        }
        this.onDispatched(traceIdOfFailedRequest, error.status);
      },
    });
  }

  private explainMissingTrace(status: number): void {
    if (environment.isDemoMode || status === 0) {
      return;
    }

    this.missingTrace.set({
      key: status >= 500 ? 'action.noTraceHeaderServerError' : 'action.noTraceHeader',
      status,
    });
  }

  private messageForFailureWithoutTrace(): TranslationKey {
    return environment.isDemoMode ? 'action.demoCannotSend' : 'action.requestFailed';
  }

  private onDispatched(traceId: string | null, status: number): void {
    this.sending.set(false);
    this.lastTraceId.set(traceId);
    if (traceId) {
      this.traceDispatched.emit(traceId);
    } else {
      this.explainMissingTrace(status);
    }
  }
}
