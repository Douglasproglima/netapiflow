import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { MatDialogRef } from '@angular/material/dialog';
import { AboutDialogComponent } from './about-dialog.component';

describe('AboutDialogComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('reads the version from GET /config, not a build-time constant (D-44)', async () => {
    const fixture = TestBed.createComponent(AboutDialogComponent);
    await fixture.whenStable();

    httpMock.expectOne('api/config').flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
    await fixture.whenStable();

    expect(fixture.componentInstance.version()).toBe('0.1.0');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('0.1.0');
  });
});
