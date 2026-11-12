const BASE_MARGIN_PX = 40;
const OVERLAY_EDGE_INSET_PX = 12;

export interface ContainerSize {
  readonly width: number;
  readonly height: number;
}

/**
 * The legend and zoom controls are opaque overlays pinned to the bottom corners (guide seção 8:
 * "achado real da Fase 25"), outside the SVG's own layout. Reserving a bottom margin sized to the
 * taller of the two keeps every real node above that strip, regardless of which corner a node
 * would otherwise land in after auto-fit.
 */
export function computeGraphViewBox(
  contentWidth: number,
  contentHeight: number,
  container: ContainerSize,
  overlayPanelHeightsPx: readonly number[],
): string {
  if (contentWidth === 0 && contentHeight === 0) {
    return '0 0 100 100';
  }

  const width = contentWidth + BASE_MARGIN_PX;
  const tallestOverlayPx =
    overlayPanelHeightsPx.length > 0 ? Math.max(...overlayPanelHeightsPx) : 0;
  const reservedBottomPx = tallestOverlayPx > 0 ? tallestOverlayPx + OVERLAY_EDGE_INSET_PX : 0;

  const bottomMargin =
    reservedBottomPx > 0 && container.width > 0 && container.height > 0
      ? bottomMarginForOverlay(width, contentHeight, container, reservedBottomPx)
      : BASE_MARGIN_PX;

  return `0 0 ${width} ${contentHeight + bottomMargin}`;
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

  const denominator = container.height - reservedBottomPx;
  if (denominator <= 0) {
    return BASE_MARGIN_PX + reservedBottomPx;
  }

  return (BASE_MARGIN_PX * container.height + reservedBottomPx * contentHeight) / denominator;
}
