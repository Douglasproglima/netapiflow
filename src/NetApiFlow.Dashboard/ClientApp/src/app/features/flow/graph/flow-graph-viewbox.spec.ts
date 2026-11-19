import { computeGraphViewBox, isLegendCompact } from './flow-graph-viewbox';

function parseViewBox(viewBox: string): { width: number; height: number } {
  const [, , width, height] = viewBox.split(' ').map(Number);
  return { width, height };
}

describe('computeGraphViewBox', () => {
  it('returns the placeholder box when there are no nodes', () => {
    expect(computeGraphViewBox(0, 0, { width: 0, height: 0 }, [])).toBe('0 0 100 100');
  });

  it('falls back to the base margin when the container has not been measured yet', () => {
    const viewBox = computeGraphViewBox(400, 100, { width: 0, height: 0 }, [60, 26]);

    expect(viewBox).toBe('0 0 440 140');
  });

  it('falls back to the base margin when no overlay panel is tall enough to matter', () => {
    const viewBox = computeGraphViewBox(400, 100, { width: 1000, height: 300 }, [0, 0]);

    expect(viewBox).toBe('0 0 440 140');
  });

  it('reserves exactly the overlay footprint in screen pixels when width governs the fit', () => {
    const container = { width: 1000, height: 2000 };
    const viewBox = computeGraphViewBox(400, 100, container, [60, 26]);
    const { width, height } = parseViewBox(viewBox);

    const scale = Math.min(container.width / width, container.height / height);
    const bottomMarginUserUnits = height - 100;
    const bottomMarginScreenPx = bottomMarginUserUnits * scale;

    expect(scale).toBeCloseTo(container.width / width, 10);
    expect(bottomMarginScreenPx).toBeCloseTo(40 * scale + 72, 6);
  });

  it('reserves exactly the overlay footprint in screen pixels when height governs the fit', () => {
    const container = { width: 1000, height: 300 };
    const viewBox = computeGraphViewBox(400, 100, container, [60, 26]);
    const { width, height } = parseViewBox(viewBox);

    const scale = Math.min(container.width / width, container.height / height);
    const reservedScreenPx = (height - 100 - 40) * scale;

    expect(scale).toBeCloseTo(container.height / height, 10);
    expect(reservedScreenPx).toBeCloseTo(72, 6);
  });

  it('stays bounded when the canvas is barely taller than the reserved overlay footprint (replay bar eating vertical space)', () => {
    // reservedBottomPx would be 200 + 12 = 212, more than the container itself (250) leaves
    // once halved by the cap below: capped to 125, keeping the fit scale close to 1 instead of
    // the ~0.34 a margin sized off the raw, uncapped 212 would have produced.
    const container = { width: 1000, height: 250 };
    const viewBox = computeGraphViewBox(800, 60, container, [200, 30]);
    const { width, height } = parseViewBox(viewBox);

    expect(height).toBeCloseTo(205, 6);
    const scale = Math.min(container.width / width, container.height / height);
    expect(scale).toBeGreaterThan(1);
  });

  it('caps how much of the canvas an oversized overlay can reserve, instead of shrinking the graph to a sliver (replay bar + tall legend)', () => {
    // The exact shape reported live: replaying a trace shrinks the canvas height to
    // ~212px while the state legend alone is ~200px tall. Reserving the full overlay footprint
    // would leave only ~34% scale (a tiny cluster of nodes); capped at half the canvas, the
    // graph stays close to its natural size.
    const container = { width: 1110, height: 212 };
    const viewBox = computeGraphViewBox(895, 70, container, [200, 44]);
    const { width, height } = parseViewBox(viewBox);

    const scale = Math.min(container.width / width, container.height / height);
    expect(scale).toBeGreaterThan(0.85);
  });

  it('clamps the blow-up instead of growing without bound as the denominator approaches zero', () => {
    // container.height = 213 leaves a denominator of just 1 (213 - reservedBottomPx of 212). The
    // pre-fix formula divided by that raw, unclamped denominator and would have returned a
    // margin of (40 * 213 + 212 * 60) / 1 = 21,240 here — this is what the clamp must avoid.
    const unclampedReferenceMargin = (40 * 213 + 212 * 60) / 1;

    const viewBox = computeGraphViewBox(800, 60, { width: 1000, height: 213 }, [200, 30]);

    expect(parseViewBox(viewBox).height).toBeLessThan(unclampedReferenceMargin / 10);
  });

  it('uses the tallest overlay panel, not the sum of all panels', () => {
    const container = { width: 1000, height: 300 };
    const viewBoxOneTall = computeGraphViewBox(400, 100, container, [60, 26]);
    const viewBoxBothTall = computeGraphViewBox(400, 100, container, [60, 60]);

    expect(viewBoxOneTall).toBe(viewBoxBothTall);
  });
});

describe('isLegendCompact', () => {
  it('is compact on a canvas shorter than the legend column can clear (reproduced at 238px)', () => {
    expect(isLegendCompact(238)).toBe(true);
    expect(isLegendCompact(1)).toBe(true);
  });

  it('is not compact on a canvas tall enough for the full legend column', () => {
    expect(isLegendCompact(280)).toBe(false);
    expect(isLegendCompact(422)).toBe(false);
  });

  it('is not compact before the canvas has been measured (height 0)', () => {
    expect(isLegendCompact(0)).toBe(false);
  });
});
