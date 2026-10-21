import { Component, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { map } from 'rxjs';
import { DASHBOARD_VERSION } from './core/version';
import { AboutDialogComponent } from './features/about/about-dialog.component';

const NARROW_BREAKPOINT = '(max-width: 768px)';

/**
 * Shell: a persistent topbar (hamburger, logo, version) plus a sidebar linking the four feature
 * routes (D-23, ADR-0010). The hamburger always toggles the sidebar open/closed, at any width —
 * not just below `NARROW_BREAKPOINT` — so a wide viewport can collapse it for more content space
 * too (D-31, follow-up on tarefa 7.0c). Below the breakpoint the sidebar is an overlay ('over')
 * instead of docked ('side'), since there isn't room for both side by side there; crossing the
 * breakpoint picks a sensible default (open on wide, closed on narrow) without fighting a manual
 * toggle made since.
 */
@Component({
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatListModule,
    MatButtonModule,
  ],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly dialog = inject(MatDialog);

  readonly version = DASHBOARD_VERSION;

  readonly isNarrow = toSignal(
    this.breakpointObserver.observe(NARROW_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  readonly sidenavOpened = signal(true);

  constructor() {
    effect(() => {
      this.sidenavOpened.set(!this.isNarrow());
    });
  }

  toggleSidenav(): void {
    this.sidenavOpened.update((opened) => !opened);
  }

  closeSidenavIfNarrow(): void {
    if (this.isNarrow()) {
      this.sidenavOpened.set(false);
    }
  }

  openAbout(): void {
    this.dialog.open(AboutDialogComponent, { width: '460px', maxWidth: '90vw' });
  }
}
