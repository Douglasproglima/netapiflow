# generate-design-md.js

Reads `docs/brand/tokens.md` (colors, on-tint pairs, typography scale, spacing, radius) and writes
`DESIGN.md` at the repository root. `DESIGN.md` is the context Impeccable (ADR-0014) reads for the dashboard;
it is generated, never edited by hand.

Run it from `src/NetApiFlow.Dashboard/ClientApp`:

```bash
npm run generate:design-md
```

Run it whenever `docs/brand/tokens.md` or its typography scale changes, as part of the
same change, so `DESIGN.md` never diverges from the canonical source.
