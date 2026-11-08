# Contributing

## Branches and releases

- `dev` is where development happens.
- Start from current `dev` and create a short-lived branch for one reviewable change.
- Open the pull request into `dev`. Only release pull requests target `main`.
- A release merges `dev` into `main` through a pull request, then tags that commit on `main` as `vMAJOR.MINOR.PATCH`.

Do not commit directly to `main`.

## Before opening a pull request

Keep each change small and focused, and update [the specification](docs/spec.md) if behaviour changes. Do not commit credentials, customer data or nursery records.

Dependencies and bundled assets must use licences compatible with [AGPL-3.0-or-later](LICENSE).
