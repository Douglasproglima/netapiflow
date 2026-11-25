import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ImportedTraceService {
  readonly active = signal(false);
}
