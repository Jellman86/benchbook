# Review fixes 2: implementation handover

Branch: `review-fixes-2`, based on production commit `ec2ce26a89e3771a2016669b6f902b5b596e9a6b`.

Worktree: `/mnt/apps/developer/jellman86/agent-state/t3/worktrees/growlog/t3-27b280c2-ffa2f69d9225f4c5` (also available through the original `/home/jellman86/.t3/worktrees/growlog/t3-27b280c2-ffa2f69d9225f4c5` symlink).

All work is local. Nothing was pushed, opened as a pull request or deployed. Tests used throwaway databases. Product behaviour is recorded in [the specification](spec.md).

## Findings

| Finding | Result |
| --- | --- |
| 1 | Fixed. Leaflet's only tooltip binding receives a DOM element filled with `textContent`. Searches found no other popup or HTML label binding needing the same change. |
| 2 | Fixed. Authority changes invalidate associations even while disabled. OIDC account matching binds issuer and subject together. |
| 3 | Fixed. Recording captures its originating queue before awaiting. Sign-out, session expiry and person changes stop further flushes; changing people remounts Home. |
| 4 | Fixed. Both ordinary counts and count-then-transfer enforce batch membership while holding the source tray lock. |
| 5 | Fixed. New batches and transfers retain their identifiers and original times across retries. Invoice and purchase creation accept optional client IDs and serialise matching retries. |
| 6 | Fixed. Request fingerprints distinguish changed work, passport, consignment, invoice and purchase requests. Event-store calls without fingerprints compare their stored payload and lines. Changed package-passport retries also conflict. |
| 7 | Fixed. Lost-all decides whether the tray was uncounted under its lock and participates in its own settlement as a zero observation. |
| 8 | Fixed. VAT-registered invoices prefill net prices from gross catalogue prices, rounded to the penny. Rate changes recompute the prefill until a person edits the price. |
| 9 | Fixed. Passport issue locks affected trays in database ID order before validation and checks combined quantities for each batch and tray. Invoice locks follow the same order as invoice stock changes. |
| 10 | Fixed. Time alone changes origin only with explicit active-growth confirmation. Without it the original country stays. The confirmation and applied rule are retained; potting-on and the host one-year rules remain. Continuous outdoor residence is checked separately from protection. |
| 11 | Fixed. Plant and package passports, including screen previews, print E with a blank value. The conflicting A-to-D specification wording is removed. |
| 12 | Fixed. Part D uses assigned ISO alpha-2 codes. Part A requires the genus and another word, including “sp.”; the specification explicitly says this cannot establish that a name is botanical rather than a common name. |
| 13 | Fixed. Mandatory passport content never falls back to clipping. Stock too small gives a clear refusal. Tests extract real PDF text and check the required parts and a long botanical name. |
| 14 | Fixed. Package issue revalidates invoice eligibility under the invoice locks, refusing a credited invoice. |
| 15 | Fixed. Sessions record their sign-in method. Name-tap sessions are rejected when policy tightens even if the person has credentials. Credential upgrades replace the session method. |
| 16 | Fixed. A matching lost-all retry is recognised before empty-stock validation. |
| 17 | Fixed. Germination and its optional count are one transactional request and one offline queue item. Undo reverses both together, with matching undo retries recognised. |
| 18 | Fixed. Recent cutting starts include the mother ID and name, and “Again” carries both into the new request. |
| 19 | Fixed, with a mechanism correction. This checkout uses localStorage rather than IndexedDB. Waiting and failed work now share one per-person record, so fail/retry is one atomic write. Storage write failures are shown. Earlier two-key data is read and upgraded. |
| 20 | Fixed. Hierarchy moves take a table lock and walk the full ancestry, rejecting cycles, repeated ancestors and missing ancestry. Concurrent moves and chains deeper than twelve places have regressions. |
| 21 | Fixed. Export includes count adjustments with work/count references, line effective times, and recovered legacy count observations. |
| 22 | Fixed. All export reads use one REPEATABLE READ transaction. |
| 23 | Fixed. “Never reported” is healthy only during the first account's 24-hour grace period. The migration recovers existing version-7 account creation times; Home warns when no backup has ever reported. |
| 24 | Fixed. `/api/health` checks the database with a short timeout and returns 503 when unavailable. `/api/health/live` answers independently, including when authentication key loading cannot reach the database. Docker and e2e keep using readiness. |
| 25 | Fixed. The one-minute limit uses server receive time under the sensor lock; sample-time deduplication is separate. The browser fixture now supplies historical samples from separate retired test sensors and asserts a burst is refused. |
| 26 | Fixed. Printer destinations exclude the server's interface addresses, and normal settings and connections accept only 9100–9102, 515 and 631. The documented test-only `Printing:AllowLocal` exception permits pretend printers on loopback and ephemeral ports. |
| 27 | Fixed. Climate history, trace loads/searches and received-passport loads guard their responses against selection changes. The phone browser regression delivers an obsolete climate response after the current one. |
| 28 | Fixed. Invoices snapshot the nursery name, address, VAT number and contact text at issue. Credit notes carry those snapshots. Old invoices without them fall back to current settings. |
| 29 | Fixed. The historical migration still removes genuine demo accounts on a fresh database, but only with positive archived, credential-free demo markers and no roles, passkeys or external logins. A live owner with an example name is preserved in the upgrade regression. Its already-applied production effect is unchanged. |
| 30 | Fixed. Calendar defaults and comparisons use `isoDay`, including invoices, purchases, received passports, till totals and sow-by dates. Mother-plant date displays no longer slice UTC timestamps either. |
| 31 | Fixed. Typst has one application-wide process semaphore. Identical label jobs in a sheet share one render. |
| 32 | Fixed. A same-day nudge uses database-representable microseconds after the start, before later observations and without crossing midnight. If there is no room it refuses. A regression places a later count seconds before midnight. |
| 33 | Fixed. Corrected starts resolve to the original work type and effective time for batch editing and stock date checks. Tray date limits also use line effective times. |
| 34 | Fixed after checking history. `5ab942b` stored count differences without count payloads; `bd8595a` introduced the payload. The migration reconstructs earlier observations in a separate table without updating immutable events, and a regression proves they absorb late work. |
| 35 | Fixed within the requested scope. Consignment and passport issue screens show the separate supplier-document reminder; batch reminders use source/stage and propagating pot-size names. Creating the supplier document remains in the specification's “still to come”. |
| 36 | Fixed. Climate readings and its reactive clock, and Sensors' last-heard clock, refresh every three minutes and when the document becomes visible. |
| 37 | Fixed. Customer Change opens the phone FormPanel, scrolls to its `data-edit-form` and focuses Name. Covered at 390 px. |
| 38 | Skipped as permitted. The figures combine corrections, first counts and count adjustments across the ledger. No cheap SQL reduction was clearly safe, so the figures query is unchanged. |

The passport rule changes were checked against [GOV.UK's passport guidance](https://www.gov.uk/guidance/issuing-plant-passports-to-trade-plants-in-great-britain).

## Migration and compatibility

One migration was added: `20261113081808_ReviewSafetyAndRetryRecords`.

It adds nullable request fingerprints, nullable invoice issuer snapshots, nullable active-growth evidence and sensor receive time, and a separate legacy-count table. Account creation time has a safe database default and existing version-7 identifiers are backfilled. Existing event records are never updated or deleted. The legacy-count backfill reconstructs stock as it stood when each old count was recorded.

The existing `RemoveLeftoverExamplePeople` migration was changed only for the explicitly requested fresh-database safety check; no attempt was made to undo its production effect.

Two conservative compatibility choices need review:

- Old subject-only OIDC associations are not trusted as issuer-bound associations. People with those associations must link their staff login again while signed in another way; the owner can provide that other way.
- Stateful retries against old events without a fingerprint may be refused as `IdInUse` when the original request cannot be proven. They do not silently create another record. Inspect the existing work before recording it again.

## Verification

| Check | Final result |
| --- | --- |
| `dotnet build -c Release` | Passed; 0 warnings, 0 errors. |
| Full backend suite with the supplied Typst binary and `timeout 1200` | 499 total; 499 passed, 0 failed, 0 errors, 0 skipped, 0 not run; 273.385 seconds. |
| `npm run check` | 0 errors, 0 warnings. |
| `npx vitest run` | 183 tests passed across 32 files; 0 failures. |
| `npm run build` | Passed. |
| `npx playwright test` | 2 passed, 0 failed; Chromium, including 390 px phone checks; 1.5 minutes. |
| `dotnet format --verify-no-changes` | Passed. |
| EF pending-model check with the requested design connection string | Passed: no changes since the last migration. |
| API generation | Release OpenAPI build and `npm run generate:api` passed; both generated files committed. |
| iOS Safari / WebKit | Could not run. The WebKit launch probe failed because this host lacks its required native libraries. No system packages were installed. Chromium phone checks do not establish Safari compatibility. |

Build output, test results, downloaded WebKit files, temporary files and task caches are inside this worktree and ignored by Git. The supplied Typst binary and already-installed Chromium were used in place.
