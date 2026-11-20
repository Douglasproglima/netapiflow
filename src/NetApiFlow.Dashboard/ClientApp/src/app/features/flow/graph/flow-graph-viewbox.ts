const BASE_MARGIN_PX = 40;
const OVERLAY_EDGE_INSET_PX = 12;
const MAX_RESERVED_FRACTION = 0.5;
const LEGEND_COMPACT_CANVAS_HEIGHT_PX = 280;

export interface ContainerSize {
  readonly width: number;
  readonly height: number;
}

/**
 * Below this canvas height, the six-item legend's own full column (guide seção 8) is taller than
 * half the canvas, the point past which `computeGraphViewBox` gives up on fully clearing it and
 * a real node can render behind it (reproduced live at a 238px canvas). Icons only, in a wrapped
 * row (`FlowGraphComponent` applies the class), cut the legend's footprint enough that the
 * auto-fit margin can clear it again.
 */
export function isLegendCompact(canvasHeightPx: number): boolean {
  return canvasHeightPx > 0 && canvasHeightPx < LEGEND_COMPACT_CANVAS_HEIGHT_PX;
}

/**
 * The legend and zoom controls are opaque overlays pinned to the canvas corners, outside the
 * SVG's own layout. Margins sized to the tallest overlay on each edge keep every real node clear
 * of them, whichever corner a node would otherwise land in after auto-fit.
 */
export function computeGraphViewBox(
  contentWidth: number,
  contentHeight: number,
  container: ContainerSize,
  bottomOverlayHeightsPx: readonly number[],
  topOverlayHeightsPx: readonly number[] = [],
): string {
  if (contentWidth === 0 && contentHeight === 0) {
    return '0 0 100 100';
  }

  const width = contentWidth + BASE_MARGIN_PX;
  const uncappedBottomPx = reservedPxFor(bottomOverlayHeightsPx);
  const uncappedTopPx = reservedPxFor(topOverlayHeightsPx);
  const uncappedTotalPx = uncappedBottomPx + uncappedTopPx;

  // A canvas squeezed short enough (the replay bar eating vertical space, e.g.) can leave the
  // overlays taller than the canvas itself: fully clearing them would shrink the graph to a
  // sliver. Past half the canvas, settle for a bounded, partial clearance instead.
  const cappedTotalPx = Math.min(uncappedTotalPx, container.height * MAX_RESERVED_FRACTION);
  const capFactor = uncappedTotalPx > 0 ? cappedTotalPx / uncappedTotalPx : 0;
  const reservedBottomPx = uncappedBottomPx * capFactor;
  const reservedTopPx = uncappedTopPx * capFactor;
  const reservedPx = reservedBottomPx + reservedTopPx;

  if (reservedPx <= 0 || container.width <= 0 || container.height <= 0) {
    return `0 0 ${width} ${contentHeight + BASE_MARGIN_PX}`;
  }

  const extraMargin =
    bottomMarginForOverlay(width, contentHeight, container, reservedPx) - BASE_MARGIN_PX;
  const topMargin = (extraMargin * reservedTopPx) / reservedPx;
  const bottomMargin = BASE_MARGIN_PX + (extraMargin * reservedBottomPx) / reservedPx;

  return `0 ${-topMargin} ${width} ${contentHeight + bottomMargin + topMargin}`;
}

function reservedPxFor(overlayHeightsPx: readonly number[]): number {
  const tallestPx = overlayHeightsPx.length > 0 ? Math.max(...overlayHeightsPx) : 0;
  return tallestPx > 0 ? tallestPx + OVERLAY_EDGE_INSET_PX : 0;
}

/**
 * The SVG scales to the container under the default `preserveAspectRatio="xMidYMid meet"`, so
 * whichever axis has the smaller container/content ratio governs the actual on-screen scale.
 * Solved as a closed form per axis instead of iterating: the margin itself changes the content
 * height, which can change which axis governs, so each candidate verifies its own assumption.
 */
function bottomMarginForOverlay(
  width: number,
  contentHeight: number,
  container: ContainerSize,
  reservedBottomPx: number,
): number {
  const widthConstrainedMargin = BASE_MARGIN_PX + (reservedBottomPx * width) / container.width;
  const scaleIfWidthConstrained = container.width / width;
  const heightAtThatScale = contentHeight + widthConstrainedMargin;
  const scaleIfHeightConstrained = container.height / heightAtThatScale;

  if (scaleIfWidthConstrained <= scaleIfHeightConstrained) {
    return widthConstrainedMargin;
  }

  // A denominator near zero (the canvas is barely taller than the reserved overlay footprint,
  // e.g. the replay bar eating vertical space) would otherwise blow this margin up without
  // bound. Flooring it at BASE_MARGIN_PX caps how cramped the result can get instead.
  const denominator = Math.max(container.height - reservedBottomPx, BASE_MARGIN_PX);

  return (BASE_MARGIN_PX * container.height + reservedBottomPx * contentHeight) / denominator;
}
