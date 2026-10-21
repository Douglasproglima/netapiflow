import { Component, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule, MatSidenav } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { map } from 'rxjs';
import { DASHBOARD_VERSION } from './core/version';
import { AboutDialogComponent } from './features/about/about-dialog.component';

const NARROW_BREAKPOINT = '(max-width: 768px)';

/**
 * Shell: sidebar linking the four feature routes, plus the router outlet (D-23, ADR-0010). Below
 * `NARROW_BREAKPOINT` (tablet/phone widths, D-31 item 4) the sidebar becomes an overlay opened by
 * a hamburger button instead of staying permanently docked — there isn't room for both the
 * sidebar and a feature's own content side by side.
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
  private readonly sidenav = viewChild.required(MatSidenav);

  readonly version = DASHBOARD_VERSION;

  readonly isNarrow = toSignal(
    this.breakpointObserver.observe(NARROW_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  toggleSidenav(): void {
    this.sidenav().toggle();
  }

  closeSidenavIfNarrow(): void {
    if (this.isNarrow()) {
      this.sidenav().close();
    }
  }

  openAbout(): void {
    this.dialog.open(AboutDialogComponent, { width: '420px' });
  }
}
