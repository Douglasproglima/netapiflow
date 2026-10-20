import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';

/**
 * Shell: fixed sidebar linking the four feature routes, plus the router outlet (D-23, ADR-0010).
 * Replaces the earlier horizontal toolbar nav (tarefa 7.0b) — still real routing, just a
 * different position and visual shape.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatListModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {}
