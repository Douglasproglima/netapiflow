# The finishing of the panel (Phase 60, Part A)

Proves, in a real browser, what a unit test cannot see in this part of Phase 60: the layout of the panel
on a phone and the position of "Send request" in the drawer (jsdom has no layout), the exported SVG
opened on its own, and the response block of the Runner against a real `fetch`. The texts and the states
have unit tests too (`endpoint-form.response.spec.ts`, `flow-page.component.spec.ts`, and the others of
the pull request). Everything here is run by hand.

## Running it

From `src/NetApiFlow.Dashboard/ClientApp`:

```bash
npm ci
npm run build:demo
npm run build
python -I ../../../tests/manual-verification/Fase68AcessibilidadeVerification/serve-spa.py dist/demo/browser 4872
python -I ../../../tests/manual-verification/Fase68AcessibilidadeVerification/serve-spa.py dist/dashboard/browser 4873
```

The demo on `4872` answers every call with a recording, so it shows the screens as a user sees them. It
has no recording of `GET /architecture` for a window that starts now, and it does not call a real host, so
the Architecture and the Runner response were measured on the production build on `4873`, with the calls of
the panel answered by `page.route` inside the browser. To compare with the code before the change, build
`develop` the same way, copy `dist/demo/browser` aside, and serve the copy on `4874`.

The scripts run through the Playwright MCP, as `browser_run_code_unsafe` with `filename`. Each one has a
`stage` constant, `antes` or `depois`, that names its screenshots:

- `send-button-rect.js`: Task 60.12.
- `mobile-layout.js`: Task 60.1.
- `flow-texts.js`: Tasks 60.2, 60.3 and 60.6 on the demo, `antes` on `4874` and `depois` on `4872`.
- `runner-response.js` and `runner-big-body.js`: Task 60.3 against a real `fetch`.
- `architecture-export.js`: Task 60.7.
- `detect-matrix.mjs`: the detector over the matrix below.

## 60.12: "Send request" in the drawer

The drawer was `position: fixed` with `top: 56px` and `bottom: 1rem`, but the host of the form also had
`height: 100%`, and an explicit height wins over `bottom`. So the drawer was as tall as the window and
started 56 px below the top, and its last 56 px were below the screen. Measured as the rectangle of the
button against the window, for a `GET` with a path parameter, a `POST` with a body and a `POST` with both,
in both themes (the 18 combinations gave the same numbers within each width):

| Window | Bottom of the drawer, before | Button, before | Visible, before | Bottom of the drawer, after | Button, after | Visible, after |
|---|---|---|---|---|---|---|
| 390x844 | 900 | 839 to 883 | 5 of 44 px | 828 | 767 to 811 | 44 of 44 px |
| 768x1024 | 1080 | 1019 to 1063 | 5 of 44 px | 1008 | 947 to 991 | 44 of 44 px |
| 1023x768 | 824 | 763 to 807 | 5 of 44 px | 752 | 691 to 735 | 44 of 44 px |

Before, neither `Tab` nor scrolling the form brought it into view, and the scroll of the document stayed
at 0. After, it is fully inside the window at rest, after scrolling the form and after `Tab`. The fix is
`height: auto` on the drawer, so that `top` and `bottom` decide its height. Screenshots kept: the `GET` at
each width in the dark theme, and the `POST` with a parameter and a body at 390 in the light theme, before
and after (`*-60-12-get-*-dark.png`, `*-60-12-post-com-parametro-390-light.png`). The script took all 36.

## 60.1: the panel on a phone (390x844)

Before, each of these needed a sideways scroll; now rows stack, and the labels of the columns that are
no longer headers are written in each row.

| Screen | Before | After |
|---|---|---|
| Compare | the table was 639 px wide in a 356 px box; column B ended at 564 and Delta at 656, in a 390 px window | no sideways scroll (341 of 341 px); A, B and Delta are rows of a card, between x 33 and 342 |
| Insights | the p95 column ended at 476, in a 390 px window | no sideways scroll; "Executions" and "p95" are lines of the row, between x 61 and 314 |
| History | the Compare button was at x 424 to 516; the route ended in an ellipsis (259 px of text in 138) | the button is at x 29 to 120; the route wraps (138 of 138 px) |

The tables keep their roles (`table`, `row`, `cell`, `columnheader`), because turning a table into blocks
makes some browsers drop its semantics. Screenshots: `antes-60-1-*` and `depois-60-1-*-dark.png` (the script
also took the light theme).

## 60.2, 60.4, 60.5, 60.6 and 60.8: texts and states

On the slow trace `f7cebccd…` of the demo, at 1440x900, in Portuguese:

| What | Before | After |
|---|---|---|
| "Slowest step" banner | `OrdersController · 321.7 ms` | `OrderService · 315.6 ms de tempo próprio` |
| Unexplained time | `0% do tempo (1.2 ms de 322.9 ms)` | `<1% do tempo (1.2 ms de 322.9 ms)` |
| Name of the request summary | `Request summary` | `Resumo da requisição` |
| Tooltip of the route, header and summary | none | the whole route |
| Rows of the "Etapas" list that are buttons | 0 | 4 |
| Accessible name of the button of the drawer at 390x844 | none (its text is hidden) | `Testar um endpoint` |

"Slowest step" is the step with the most time of its own (its duration minus that of its direct children,
never below 0). In the demo trace the banner used to name `OrdersController`, whose duration includes
the steps it calls, and now names `OrderService`, where the time is spent. The error origin, the status of a step, the chip of an imported
trace, the notice of the third selection and the "Slow only" switch are covered by unit tests, and the
detector walked those screens. Screenshots: `antes-60-2-fluxo-1440.png` and `depois-60-2-fluxo-1440.png`.

## 60.3: the Runner

On the production build with the calls answered inside the browser (`runner-response.js`), in the dark
theme at 1440x900:

| Answer | What the block shows |
|---|---|
| `200`, `application/json` | `HTTP 200`, and the JSON text |
| `200`, `text/html` with `<img src=x onerror=alert(1)><b>bold</b>` | `HTTP 200`, and that exact text. There is no `img` and no `b` in the block |
| `200`, `application/octet-stream` | `HTTP 200`, and `(conteúdo application/octet-stream, não exibido)` |
| `500`, `text/plain` | `HTTP 500`, and the text of the failure |
| `204` | `HTTP 204`, and `(corpo vazio)` |

The answers carried `Set-Cookie: session=top-secret`, and the text of the page never held it. The trace
id was a link to `/?trace=<id>` every time, "Send request" stayed inside the window, and a `POST` started
with the `example` of its request body. On the demo (`flow-texts.js`), the label of the field went from
`idcaminho · integer` to `id caminho · integer`, and a send showed `HTTP 200`, the body and the link.

**A 5 MB body** (`runner-big-body.js`), measured with a `PerformanceObserver` of long tasks in the page:

| Body | Longest task of the main thread | Heap after | Characters in the page |
|---|---|---|---|
| 1 MB | 50 ms | 21 MB | 4000 |
| 5 MB | 282 ms | 32 MB | 4000 |
| 5 MB, again | 248 ms | 32 MB | 4000 |

The 250 to 280 ms is the browser reading the body as text, which the Runner already did before this
change. Keeping the first 4000 characters costs nothing visible, and nothing of the rest stays.

**The form race (not reproduced).** The audit saw the route field come back empty a few tens of
milliseconds after the endpoint was picked, 1 time in 25. I could not reproduce it, and the audit
decided to leave it out of 0.6.0. What I ran, for the record: 100 attempts, every one starting from a page
that had just been loaded (`page.goto`), never from a page that was already open. Browser: Chromium
158.0.8127.37 from the Playwright MCP, on Windows 10. Backend: the static demo, the build of `develop` at
`3ed8b98` (`main-5SGTYKB6.js`), so no real host. 40 attempts on `/runner` without throttling, with a delay
of 0, 10, 20, 30 or 50 ms between picking the endpoint and filling the field; 60 attempts with the CPU
limited to 4 times, 30 on `/runner` and 30 in the drawer of the Flow screen, with a delay of 0 to 100 ms.
In every one the field held `5` and no message said "Preencha o parâmetro de rota".

## 60.7: the Architecture

On a map of five steps in which `SELECT items` runs 15 times in 3 executions (`architecture-export.js`):

| What | Before | After |
|---|---|---|
| Legend | 6 node states (waiting, running, completed, slow, error, not executed) | none |
| The step that runs 5 times in each execution | `SELECT items` | `SELECT items (×5)` |
| Exported SVG: `_ngcontent` attributes | 75 | 0 |
| Exported SVG, opened on its own: font size of the texts | 16 px for all of them | 12, 14 and 11 px, as in the panel |

The exported file, opened on its own, overlapped its texts before and now looks like the panel
(`antes-60-7-exportado-aberto-sozinho.png` and `depois-60-7-exportado-aberto-sozinho.png`).

## The detector

`npx impeccable detect`, through Edge with the theme and the width emulated by CDP, on six screens (the
Flow screen with a trace, the Flow screen with the drawer open, History, Compare, Insights and the Runner
after a send, with the response block open), in both themes at `1440x900` and at `390x844`: 24 scans on
the code before the change and 24 after (`detect-matrix.mjs`, the same script of the Phase 68 with the
screens of this phase). Each page was probed for its width, its theme and that it had drawn. In the 4 scans
of the Runner after the change, the response block was on the page.

| Finding | Before | After |
|---|---|---|
| Scans | 24 | 24 |
| `low-contrast` (blocks) | 0 | 0 |
| `undersized-ui-text` (blocks) | 0 | 0 |
| `flat-type-hierarchy` (warning) | 4: Insights, in both themes and at both widths | 4, the same |
| `straggler` (warning) | 4: the open drawer at `390x844` and Insights at `390x844`, in both themes | 4, the same |

The Architecture screen is not in the matrix, because the demo has no recording for it. It was measured
in the browser instead, above. This change did not add or remove any finding.

## Files

- The scripts named above, and `serve-spa.py` of `Fase68AcessibilidadeVerification`, which they use.
- `antes-*.png`, `depois-*.png` and the two exported `*.svg`: the evidence.
