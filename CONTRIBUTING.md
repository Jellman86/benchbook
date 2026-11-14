# Contributing

## Branches and releases

- `dev` is where development happens.
- Start from current `dev` and create a short-lived branch for one reviewable change.
- Open the pull request into `dev`. Only release pull requests target `main`.
- A release merges `dev` into `main` through a pull request, then tags that commit on `main` as `vMAJOR.MINOR.PATCH`.

Do not commit directly to `main`.

## Backend tests and migrations

The backend tests start a throwaway PostgreSQL container, so they need Docker. They also print real labels, so they need [Typst](https://typst.app) on the path, or `Typst__Path` set to it.

To add a migration, run `dotnet tool restore`, then:

```sh
ConnectionStrings__Hortiplanner=Host=localhost dotnet ef migrations add <Name> --project src/Hortiplanner.Api --output-dir Data/Migrations
```

The command only needs a connection string to be set. It does not connect to a database. The application applies migrations when it starts.

Once a migration is merged it may already have run somewhere, so never edit it: add a new one. A new migration must also be newer than every migration on the target branch, so if another one lands first, update your branch and regenerate yours. CI enforces both. Prefer changes that keep existing data, and split anything destructive into separate releases.

## Browser tests

`npm run e2e` in `web` drives the real application in Chromium, against a throwaway PostgreSQL container, using a built-in virtual passkey. Build the web app first with `npm run build`, and install the browsers once with `npx playwright install chromium webkit`.

Staff mostly use iPhones, so the phone specs (`iphone`, `ask` and `review-phone`) also run in WebKit, Safari's engine, at iPhone 15 size, as the `iphone` project. Check every screen change at iPhone width. Where WebKit can't run on the machine itself, start Playwright's server in its official image and point the tests at it:

```sh
docker run -d --rm --name hortiplanner-webkit --network host --init mcr.microsoft.com/playwright:v1.65.0-noble \
  /bin/sh -c "cd /tmp && npx -y playwright@1.65.0 run-server --port 3123 --host 127.0.0.1"
PW_WEBKIT_WS=ws://127.0.0.1:3123/ npx playwright test --project=iphone
```

## API contract

The web client is generated from the API. After changing an endpoint, refresh it, and commit both generated files in `web/src/api`:

```sh
dotnet build src/Hortiplanner.Api -p:OpenApiGenerateDocumentsOnBuild=true
npm --prefix web run generate:api
```

## Before opening a pull request

Keep each change small and focused, and update [the specification](docs/spec.md) if behaviour changes. Do not commit credentials, customer data or nursery records.

Dependencies and bundled assets must use licences compatible with [AGPL-3.0-or-later](LICENSE).
