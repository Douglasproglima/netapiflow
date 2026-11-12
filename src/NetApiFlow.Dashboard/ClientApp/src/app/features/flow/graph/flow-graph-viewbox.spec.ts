import { computeGraphViewBox } from './flow-graph-viewbox';

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

  it('uses the tallest overlay panel, not the sum of all panels', () => {
    const container = { width: 1000, height: 300 };
    const viewBoxOneTall = computeGraphViewBox(400, 100, container, [60, 26]);
    const viewBoxBothTall = computeGraphViewBox(400, 100, container, [60, 60]);

    expect(viewBoxOneTall).toBe(viewBoxBothTall);
  });
});
