import { Injectable, OnDestroy, computed, signal } from '@angular/core';
import { FlowSpan, FlowTraceDetail } from '../../../core/models';

export type PlaybackState = 'paused' | 'playing' | 'finished';

/** Below this, a step would flash by too fast to notice — the cadence, never the reported duration, adapts. */
const MIN_VISUAL_DELAY_MS = 150;

/**
 * Replays a completed trace's spans in the order they really started. Only the pacing of the
 * animation changes with playback speed; the duration any panel displays always comes straight
 * from the span's own `durationMicros`, never adjusted (seção 7.3 do prompt).
 */
@Injectable()
export class ReplayPlayerService implements OnDestroy {
  private readonly traceSignal = signal<FlowTraceDetail | null>(null);
  private readonly stepIndexSignal = signal(0);
  private readonly speedSignal = signal(1);
  private readonly stateSignal = signal<PlaybackState>('paused');

  readonly state = this.stateSignal.asReadonly();
  readonly speed = this.speedSignal.asReadonly();
  readonly stepIndex = this.stepIndexSignal.asReadonly();

  /** Every span of the loaded trace, ordered by start — the one true order, never changed by speed. */
  readonly orderedSpans = computed<FlowSpan[]>(() => {
    const trace = this.traceSignal();
    return trace ? [...trace.spans].sort((a, b) => a.sequence - b.sequence) : [];
  });

  /** The spans revealed so far, in order. Each one keeps its own real `durationMicros` untouched. */
  readonly visibleSpans = computed(() => this.orderedSpans().slice(0, this.stepIndexSignal()));

  private timer: ReturnType<typeof setTimeout> | null = null;

  load(trace: FlowTraceDetail): void {
    this.clearTimer();
    this.traceSignal.set(trace);
    this.stepIndexSignal.set(0);
    this.stateSignal.set('paused');
  }

  setSpeed(speed: number): void {
    this.speedSignal.set(speed);
  }

  play(): void {
    if (this.stateSignal() === 'playing') {
      return;
    }

    if (this.stepIndexSignal() >= this.orderedSpans().length) {
      this.stepIndexSignal.set(0);
    }

    this.stateSignal.set('playing');
    this.scheduleNext();
  }

  pause(): void {
    this.clearTimer();
    if (this.stateSignal() !== 'finished') {
      this.stateSignal.set('paused');
    }
  }

  restart(): void {
    this.clearTimer();
    this.stepIndexSignal.set(0);
    this.stateSignal.set('paused');
  }

  stepForward(): void {
    this.clearTimer();
    this.advanceStep();
    this.stateSignal.set(
      this.stepIndexSignal() >= this.orderedSpans().length ? 'finished' : 'paused',
    );
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  private scheduleNext(): void {
    const totalSpans = this.orderedSpans().length;
    if (this.stepIndexSignal() >= totalSpans) {
      this.stateSignal.set('finished');
      return;
    }

    const delayMs = this.computeDelayMs(this.stepIndexSignal());
    this.timer = setTimeout(() => {
      this.advanceStep();
      if (this.stateSignal() === 'playing') {
        this.scheduleNext();
      }
    }, delayMs);
  }

  private computeDelayMs(index: number): number {
    const spans = this.orderedSpans();
    const speed = this.speedSignal();
    if (index === 0) {
      return MIN_VISUAL_DELAY_MS / speed;
    }

    const previousStartMs = Date.parse(spans[index - 1].startedAt);
    const currentStartMs = Date.parse(spans[index].startedAt);
    const realGapMs = Math.max(currentStartMs - previousStartMs, 0);
    return Math.max(realGapMs / speed, MIN_VISUAL_DELAY_MS / speed);
  }

  private advanceStep(): void {
    this.stepIndexSignal.update((index) => Math.min(index + 1, this.orderedSpans().length));
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
