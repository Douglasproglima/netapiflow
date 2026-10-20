import { Component, computed, inject, input, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { OpenApiDocument } from '../../core/models';
import { LiveTracePlayerService } from '../flow/playback/live-trace-player.service';
import { buildEndpointForm, listOpenApiEndpoints } from './openapi-form';

/**
 * Lets the user pick an endpoint from the host application's own OpenAPI document, fill in its
 * path/query parameters and JSON body, dispatch it, and follow the resulting trace live by
 * reusing LiveTracePlayerService (tarefa 4.7). `openApiUrl` is root-relative on purpose: the
 * host's OpenAPI document and the endpoints it describes live at the application root, which is
 * unrelated to wherever the dashboard itself is mounted (unlike ApiService's own relative URLs).
 */
@Component({
  selector: 'app-runner',
  templateUrl: './runner.component.html',
  styleUrl: './runner.component.scss',
  providers: [LiveTracePlayerService],
})
export class RunnerComponent {
  private readonly api = inject(ApiService);
  readonly livePlayer = inject(LiveTracePlayerService);

  readonly openApiUrl = input<string>('/openapi/v1.json');

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  private readonly document = signal<OpenApiDocument | null>(null);
  readonly endpoints = computed(() => {
    const doc = this.document();
    return doc ? listOpenApiEndpoints(doc) : [];
  });

  readonly selected = signal<{ method: string; path: string } | null>(null);
  readonly form = computed(() => {
    const doc = this.document();
    const selected = this.selected();
    return doc && selected ? buildEndpointForm(doc, selected.method, selected.path) : null;
  });

  readonly fieldValues = signal<Record<string, string>>({});
  readonly sending = signal(false);
  readonly sendError = signal<string | null>(null);
  readonly lastTraceId = signal<string | null>(null);

  constructor() {
    this.api.loadOpenApiDocument(this.openApiUrl()).subscribe({
      next: (doc) => {
        this.document.set(doc);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Failed to load the OpenAPI document.');
        this.loading.set(false);
      },
    });
  }

  selectEndpoint(method: string, path: string): void {
    this.selected.set({ method, path });
    this.fieldValues.set({});
    this.sendError.set(null);
    this.lastTraceId.set(null);
    this.livePlayer.stop();
  }

  fieldValue(name: string): string {
    return this.fieldValues()[name] ?? '';
  }

  setFieldValue(name: string, value: string): void {
    this.fieldValues.update((values) => ({ ...values, [name]: value }));
  }

  onFieldInput(name: string, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLTextAreaElement;
    this.setFieldValue(name, target.value);
  }

  send(): void {
    const form = this.form();
    if (!form) {
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
          this.sendError.set('Body is not valid JSON.');
          return;
        }
      }
    }

    const fullPath = queryParts.length > 0 ? `${path}?${queryParts.join('&')}` : path;

    this.sending.set(true);
    this.sendError.set(null);
    this.api.callEndpoint(form.method, fullPath, body).subscribe({
      next: (response) => {
        this.sending.set(false);
        const traceId = response.headers.get('X-NetApiFlow-TraceId');
        this.lastTraceId.set(traceId);
        if (traceId) {
          this.livePlayer.track(traceId);
        }
      },
      error: () => {
        this.sending.set(false);
        this.sendError.set('Request failed.');
      },
    });
  }
}
