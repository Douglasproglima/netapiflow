# Security Policy

## Supported versions

NetApiFlow is pre-1.0 (`0.x.y`). Security fixes are released against the latest `0.x` version
only. There is no long-term support branch before `1.0.0`.

## Reporting a vulnerability

If you find a security vulnerability in NetApiFlow, please **do not** open a public GitHub issue.

Report it privately through [GitHub's private vulnerability reporting](https://github.com/Douglasproglima/netapiflow/security/advisories/new)
for this repository, or by emailing the maintainer directly (see the GitHub profile
[@Douglasproglima](https://github.com/Douglasproglima) for contact details).

Please include:

- A description of the vulnerability and its potential impact.
- Steps to reproduce it, ideally with a minimal code sample.
- The affected package(s) and version(s).

You should expect an initial response within a few days. Once a fix is confirmed, a new patch
version is released and the advisory is disclosed.

## Scope

NetApiFlow is a diagnostic dashboard for ASP.NET Core applications. It never captures request
bodies, method arguments, headers, query strings, or parameterized SQL (see
[docs/adr/0007-nunca-capturar-payload.md](docs/adr/0007-nunca-capturar-payload.md)). By default,
the dashboard is only reachable in `Development` environments (see
[docs/adr/0006-dashboard-restrito-a-development.md](docs/adr/0006-dashboard-restrito-a-development.md)).
A report that the dashboard can be reached in another environment without explicit configuration
is treated as a security issue.
