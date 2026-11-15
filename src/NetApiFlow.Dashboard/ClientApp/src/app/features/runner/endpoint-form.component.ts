import { DOCUMENT } from '@angular/common';
import { Component, computed, inject, output, signal } from '@angular/core';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { OpenApiDocument } from '../../core/models';
import { httpMethodTagClass } from '../../shared/http-method-style';
import {
  buildEndpointForm,
  FormFieldKind,
  listOpenApiEndpoints,
  partitionDashboardRoutes,
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

  readonly selected = signal<{ method: string; path: string } | null>(null);
  readonly form = computed(() => {
    const doc = this.openApiDocument();
    const selected = this.selected();
    return doc && selected ? buildEndpointForm(doc, selected.method, selected.path) : null;
  });

  readonly fieldValues = signal<Record<string, string>>({});
  readonly sending = signal(false);
  readonly sendError = signal<TranslationKey | null>(null);
  readonly invalidFieldName = signal<string | null>(null);
  readonly lastTraceId = signal<string | null>(null);

  constructor() {
    this.config.config$.subscribe((config) => this.loadOpenApiDocument(config.openApiDocumentPath));
  }

  private loadOpenApiDocument(path: string): void {
    this.api.loadOpenApiDocument(path).subscribe({
      next: (doc) => {
        this.openApiDocument.set(doc);
        this.loading.set(false);
      },
      error: () => {
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

    const fullPath = queryParts.length > 0 ? `${path}?${queryParts.join('&')}` : path;
    this.dispatch(this.api.callEndpoint(form.method, fullPath, body));
  }

  private dispatch(request: Observable<HttpResponse<unknown>>): void {
    this.sending.set(true);
    this.sendError.set(null);
    request.subscribe({
      next: (response) => this.onDispatched(response.headers.get('X-NetApiFlow-TraceId')),
      error: (error: HttpErrorResponse) => {
        // A non-2xx response still carries the trace id header, so the Error scenario stays followable.
        const traceId = error.headers.get('X-NetApiFlow-TraceId');
        if (!traceId) {
          this.sendError.set('action.requestFailed');
        }
        this.onDispatched(traceId);
      },
    });
  }

  private onDispatched(traceId: string | null): void {
    this.sending.set(false);
    this.lastTraceId.set(traceId);
    if (traceId) {
      this.traceDispatched.emit(traceId);
    }
  }
}
