import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ActionDrawerService {
  private readonly document = inject(DOCUMENT);
  private opener: HTMLElement | null = null;

  readonly isDrawer = signal(false);
  readonly open = signal(false);

  toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.show();
    }
  }

  close(): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    this.returnFocusToOpener();
  }

  private show(): void {
    this.opener = this.document.activeElement as HTMLElement | null;
    this.open.set(true);
  }

  private returnFocusToOpener(): void {
    if (this.opener?.isConnected) {
      this.opener.focus();
    }
    this.opener = null;
  }
}
