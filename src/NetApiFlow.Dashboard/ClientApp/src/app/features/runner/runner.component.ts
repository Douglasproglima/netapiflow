import { Component, inject } from '@angular/core';
import { LiveTracePlayerService } from '../flow/playback/live-trace-player.service';
import { EndpointFormComponent } from './endpoint-form.component';

/**
 * Standalone `/runner` route: the endpoint form (shared with the `/flow` workspace's action
 * panel, ADR-0010) plus its own `LiveTracePlayerService`, tracking whatever trace the form just
 * dispatched.
 */
@Component({
  selector: 'app-runner',
  templateUrl: './runner.component.html',
  imports: [EndpointFormComponent],
  providers: [LiveTracePlayerService],
})
export class RunnerComponent {
  readonly livePlayer = inject(LiveTracePlayerService);

  onTraceDispatched(traceId: string): void {
    this.livePlayer.track(traceId);
  }
}
