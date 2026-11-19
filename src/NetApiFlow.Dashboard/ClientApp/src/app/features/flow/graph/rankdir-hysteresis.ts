import { ElementRef, Signal, effect, signal } from '@angular/core';

const RANKDIR_TB_ASPECT_RATIO = 1.2;
const RANKDIR_LR_ASPECT_RATIO = 0.9;

/**
 * Next dagre layout direction for a canvas whose height/width ratio just changed, keeping
 * `current` inside the dead zone between the two thresholds (guide seção 6.4). This is what
 * turns a near-square canvas resize into a single flip instead of a flicker.
 */
export function nextRankdir(current: 'LR' | 'TB', aspectRatio: number): 'LR' | 'TB' {
  if (aspectRatio >= RANKDIR_TB_ASPECT_RATIO) {
    return 'TB';
  }
  if (aspectRatio <= RANKDIR_LR_ASPECT_RATIO) {
    return 'LR';
  }
  return current;
}

/**
 * Dagre layout direction, decided by `canvasRef`'s own measured height/width ratio, not by
 * page width: a desktop canvas squeezed narrow by side panels is just as much a tall canvas as
 * a mobile one. Must be called from an injection context (e.g. a component field initializer
 * or constructor), the same way `toSignal` is.
 */
export function rankdirHysteresis(
  canvasRef: Signal<ElementRef<HTMLElement> | undefined>,
): Signal<'LR' | 'TB'> {
  const rankdir = signal<'LR' | 'TB'>('LR');

  effect((onCleanup) => {
    const canvasElement = canvasRef()?.nativeElement;
    if (!canvasElement || typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      const { width, height } = entry.contentRect;
      if (width > 0) {
        rankdir.update((current) => nextRankdir(current, height / width));
      }
    });
    observer.observe(canvasElement);
    onCleanup(() => observer.disconnect());
  });

  return rankdir.asReadonly();
}
