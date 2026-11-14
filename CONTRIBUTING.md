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

## Words on screen

Everything people read in Hortiplanner is written as a person would say it to a colleague, in plain UK English.

- **Write whole sentences, with a subject and a verb.** Not "Sown without a count and not counted since. A count when they're pricked out, or now, keeps the figures right." Instead: "It was sown without counting the seeds, so we don't know how many plants are in it yet. Counting it when you prick it out, or now if you have a minute, keeps the figures accurate."
- **Say what happened, why it matters and what to do**, in that order, and keep it short.
- **Match the number.** Write separate wording for one and for several ("It was due…" / "Each one was due…"), not "1 trays" or "(s)".
- **Use everyday words** a nursery worker would use. Avoid system words such as record, entity, status or invalid.
- **Make actions say where they go or what they do:** "Go to Production", "Remove the picture", not "Open" or "Click here".

## Buttons and links

- **No bare text links for actions.** Every action is a styled button: `button` (primary), `button.secondary`, or `button.link-button` / `a.small-button` for smaller ones. Each has a border, the surface colour and a drop shadow (`--shadow`).
- **Use an icon where it helps you recognise the action at a glance:** an arrow to go somewhere, external-link for a page outside Hortiplanner, a download icon for a PDF, undo for Undo, a bin for removing or voiding, a pencil for changing. Icons come from `@lucide/svelte`, at size 16 in small buttons, with `aria-hidden="true"`, and never replace the words.
- **The exceptions:** breadcrumbs stay text links, and the sidebar, bottom bar, Back and icon-only buttons stay flat.

## Before opening a pull request

Keep each change small and focused, and update [the specification](docs/spec.md) if behaviour changes. Do not commit credentials, customer data or nursery records.

Dependencies and bundled assets must use licences compatible with [AGPL-3.0-or-later](LICENSE).
