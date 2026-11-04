# NetApiFlow

Open source library for ASP.NET Core. It shows, live, the path every HTTP request takes through
your own API: Controller/Endpoint → Handler → Services → Repositories → SQL, with per-step
duration, where it failed, what was slow, and what never ran.

## Audience

.NET developers debugging their own API during local development. Not an end-user product, not a
marketing surface: the people looking at this screen already know the codebase and are trying to
answer "why is this request slow/failing" right now.

## What the dashboard screen is

A dense operate screen (`/flow`), not a landing page. Density is a feature: a developer
mid-incident wants the whole trace graph, the waterfall, and the error on one screen, not spread
across cards with generous whitespace. See `docs/design/flow-workspace/guide.md`.

## Hard constraints that shape every screen

- The dashboard answers 404 outside `Development` unless authorization is explicitly configured
  (ADR-0006). Never design a screen that assumes it's reachable in production by default.
- Never show a request/response payload, a header value, a query string, or a SQL parameter value
  (ADR-0007). Every screen that renders request data renders a sanitized shape, not raw input.
- The brand palette (violet/purple, ADR-0009) and the interface font (Inter) are deliberate
  decisions, not unexamined defaults. See `DESIGN.md` and ADR-0014 for the reasoning kept on
  record for tools that would otherwise flag them.
