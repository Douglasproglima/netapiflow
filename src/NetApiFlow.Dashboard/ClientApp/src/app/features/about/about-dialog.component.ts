import { Component } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { DASHBOARD_VERSION } from '../../core/version';

const REPOSITORY_URL = 'https://github.com/Douglasproglima/netapiflow';

/**
 * "About" dialog (D-31, tarefa 7.0c): version, license, repository link and the apiflow credit
 * the README already requires (tarefa 7.3), surfaced inside the dashboard itself too.
 */
@Component({
  selector: 'app-about-dialog',
  templateUrl: './about-dialog.component.html',
  styleUrl: './about-dialog.component.scss',
  imports: [MatDialogModule, MatButtonModule],
})
export class AboutDialogComponent {
  readonly version = DASHBOARD_VERSION;
  readonly repositoryUrl = REPOSITORY_URL;
}
