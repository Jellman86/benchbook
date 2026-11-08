# Contributing

## Branches and releases

- `dev` is where development happens.
- Start from current `dev` and create a short-lived branch for one reviewable change.
- Open the pull request into `dev`. Only release pull requests target `main`.
- A release merges `dev` into `main` through a pull request, then tags that commit on `main` as `vMAJOR.MINOR.PATCH`.

Do not commit directly to `main`.

## Backend tests and migrations

The backend tests start a throwaway PostgreSQL container, so they need Docker.

To add a migration, run `dotnet tool restore`, then:

```sh
ConnectionStrings__GrowLog=Host=localhost dotnet ef migrations add <Name> --project src/GrowLog.Api --output-dir Data/Migrations
```

The command only needs a connection string to be set. It does not connect to a database. The application applies migrations when it starts.

## Before opening a pull request

Keep each change small and focused, and update [the specification](docs/spec.md) if behaviour changes. Do not commit credentials, customer data or nursery records.

Dependencies and bundled assets must use licences compatible with [AGPL-3.0-or-later](LICENSE).
