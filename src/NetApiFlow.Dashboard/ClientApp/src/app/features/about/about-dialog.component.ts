import { Component, inject, signal } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

const REPOSITORY_URL = 'https://github.com/Douglasproglima/netapiflow';

/**
 * "About" dialog (D-31): version, license, repository link and the apiflow credit
 * the README already requires, surfaced inside the dashboard itself too.
 */
@Component({
  selector: 'app-about-dialog',
  templateUrl: './about-dialog.component.html',
  styleUrl: './about-dialog.component.scss',
  imports: [MatDialogModule, MatButtonModule, TranslatePipe],
})
export class AboutDialogComponent {
  private readonly api = inject(ApiService);

  readonly version = signal<string | null>(null);
  readonly repositoryUrl = REPOSITORY_URL;

  constructor() {
    this.api.getConfig().subscribe((config) => this.version.set(config.version));
  }
}
