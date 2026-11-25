# The essentials of keyboard access in the dashboard

Proves, in a real browser and with the keyboard only, three defects the final sweep of 0.6.0 found:
the focus of the Material buttons was too faint to see (B13a), the "Try an endpoint" drawer was no
dialog (B13b), and the focus fell on the page in several places, the worst being the Runner that
skipped the required `id` field (B13c). A unit test cannot show the first, because jsdom loads no
global stylesheet, and none of them can show what the Tab key really does, so this is run by hand.

## Running it

From `src/NetApiFlow.Dashboard/ClientApp`:

```bash
npm ci
npm run build:demo
python -I ../../../tests/manual-verification/Fase68AcessibilidadeVerification/serve-spa.py dist/demo/browser 4871
```

`serve-spa.py` answers a route such as `/history` with `index.html`, which `python -m http.server`
does not, and it sends `Cache-Control: no-store`. Before looking at anything, check that the browser
runs the build you just made: compare `[...document.scripts].map((s) => s.src.split('/').pop())`
with `ls dist/demo/browser | grep '^main-'`.

Then run the two scripts through the Playwright MCP, as `browser_run_code_unsafe` with `filename`:

- `focus-contrast.js` focuses one button of each Material type, by keyboard, in the dark and the
  light theme, and computes the contrast of what the focus draws from the computed styles. The
  indicator is the ring when there is one, otherwise the state layer. A theme is chosen through
  `localStorage['naf-theme']`, the way the toggle of the panel does, because a saved choice wins over
  `prefers-color-scheme`.
- `keyboard-walk.js` walks the three findings with `Tab`, `Enter` and `Escape` at `1440x900` and at
  `390x844`, in both themes, and records where the focus is after each step.

Both scripts have a `stage` constant, `antes` for a build of `develop` before this change and `depois`
for the fixed one, and write their screenshots here under that name.

## B13a: the focus of the Material buttons

Contrast of the focus indicator against the surface it is drawn on. Before, the only indicator was a
state layer of 12 percent (`outline: none`), so the number is how much the pixels change. After, it is
a ring of 2 px with an offset of 2 px in `--naf-focus` (`#8b5cf6` in the dark theme, `#6d28d9` in the
light one), measured against the background behind it. The minimum is 3 to 1.

| Material type | Where it was measured | Dark before | Dark after | Light before | Light after |
|---|---|---|---|---|---|
| `mat-icon-button` | theme toggle of the topbar | 1.21 | 4.23 | 1.15 | 6.49 |
| `mat-stroked-button` | "Import trace" | 1.09 | 4.23 | 1.21 | 6.49 |
| `mat-flat-button` | "Send request" | 1.24 | 4.23 | 1.26 | 6.49 |
| `mat-button` | "Close" of the About dialog | 1.09 | 4.23 | 1.21 | 6.49 |
| `mat-menu-item` | preferences menu at `390x844` | 1.44 | 3.85 | 1.28 | 6.10 |

The menu item draws its ring inside, with an offset of `-2px`, because the panel of the menu clips
anything outside it, and its focus tint is turned off so that the ring is not drawn over it: over the
tint the ring measured 2.7 to 1 in the dark theme. Material sets `outline: 0` on
`.mat-mdc-menu-item:focus`, which has the same specificity as `:focus-visible`, so the rule of the menu
item carries the extra class `.mat-focus-indicator`. A first version without it measured no indicator at
all on the menu item.

Screenshots: `antes-b13a-*` and `depois-b13a-*`, one per type and theme.

## B13b: the "Try an endpoint" drawer

Same result at `1440x900` and `390x844`, in both themes.

| Step | Before | After |
|---|---|---|
| `role` of the drawer | none, no `aria-modal`, no name | `dialog`, `aria-modal="true"`, named by its own title |
| Focus after `Enter` on "Try an endpoint" | stays on the button, behind the scrim | on the route filter, inside the drawer |
| 24 presses of `Tab` | 17 of them outside the drawer at `1440x900` (15 at `390x844`) | 0 outside |
| `Escape` | nothing | closes it |
| Focus after closing, by `Escape` or by the close button | the page body (only the close button closed it) | the "Try an endpoint" button |

Screenshots: `antes-b13b-gaveta-aberta-*` and `depois-b13b-gaveta-aberta-*`. Before, the focus ring is on
the button in the topbar, behind the scrim. After, it is on the filter inside the drawer.

## B13c: the focus that fell on the body

The final sweep listed four cases. Pressing `Enter` on every control of the six screens, and then on the
controls that only exist after a first step (the form of an endpoint, the open drawer), found these
five, and a sixth that is not a defect of focus (see below). Each case, with the steps and what the
focus did:

| Case | Steps | Before | After |
|---|---|---|---|
| Runner, pick an endpoint | `/runner`, `Tab` to `GET /api/orders/{id}`, `Enter` | the body. The next `Tab` went to "Send request" and skipped the required `id` field and the back button | the `id` field. The next `Tab` goes to "Add header", then "Send request" |
| Runner, back to the list | in the form, `Enter` on the back button | the body | the endpoint that was open |
| Runner, remove a custom header | add a header, `Enter` on its remove button | the body | the name field of the next header, or "Add header" when none is left |
| Drawer, close | `Escape` or `Enter` on the close button | the body | the button that opened it |
| History, sort a column | `Enter` on a column header (desktop only, `390x844` shows no sort buttons) | the body, once the table came back | the same sort button |

The sixth: opening a trace from a History row, or "View all" on the Flow screen, goes to another route,
and the focus then starts at the top of the page, as it does after a full page load. That was left as it is.

Screenshots: `antes-b13c-runner-apos-escolher-*` and `depois-b13c-runner-apos-escolher-*`.

## The detector

`npx impeccable detect`, through Edge with the theme and the width emulated by CDP, on the Flow screen
with a trace, the Flow screen with the drawer open, History, Components and the Runner, in both themes at
`1440x900` and at `390x844` (20 scans before, 20 after). Before each scan the page was probed for its
width, its theme and that it had drawn.

`detect-matrix.mjs` does it. It starts Edge headless with a debugging port, keeps one CDP session open
(`Emulation.setEmulatedMedia` for the theme and `Emulation.setDeviceMetricsOverride` for the width, which
last only while the session lives), loads each screen, clicks "Try an endpoint" for the drawer, reads
`innerWidth`, `prefers-color-scheme` and the number of nodes, and only then runs `npx impeccable detect
--json --cdp <port> --target <page>`. It needs Node 22 or later, for the global `WebSocket`, and the demo
served on port 4871:

```bash
node detect-matrix.mjs <absolute path of ClientApp> <label> <absolute folder for the Edge profile>
```

It keeps its Edge profile in the folder of the third argument and does not delete it.

| Finding | Before | After |
|---|---|---|
| Scans | 20 | 20 |
| `low-contrast` (blocks) | 0 | 0 |
| `undersized-ui-text` (blocks) | 0 | 0 |
| `straggler` (warning) | 2, the open drawer at `390x844`, in both themes | 2, the same |

The `straggler` is the intro paragraph of the drawer, whose last line holds one word. It is a warning,
it was there before, and this change did not add or remove any finding.

## Files

- `focus-contrast.js`, `keyboard-walk.js`: the two scripts above.
- `detect-matrix.mjs`: the detector over the matrix above.
- `serve-spa.py`: the static server with the fallback to `index.html`.
- `antes-*.png`, `depois-*.png`: the screenshots, before and after.
