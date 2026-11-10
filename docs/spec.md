# GrowLog: product specification

Status: draft, revised after independent review and user feedback. GrowLog is an open-source record-keeping system for small plant nurseries. The first release targets England and Wales; Scotland, which has a separate authority, is not yet verified.

## Why it exists

Small nurseries keep propagation records in spreadsheets. A spreadsheet row cannot follow a batch across trays, count what survived, or produce a plant passport and a record an inspector will accept. This replaces that with a tool quick enough to use with soil on your hands.

## Who uses it

| Person | Needs |
| --- | --- |
| Nursery worker, not comfortable with computers | Scan a label, tap what happened, get on with the day |
| Owner | See stock, survival and sales; print labels; prepare for inspections |
| Plant health inspector (indirect) | Traceability records on request |

## Principles

1. **Scan, then tap.** Scan a label, choose what happened, confirm. No menus to learn.
2. **One action per screen,** large targets, plain words. Quantities use large steppers and presets such as full tray or half tray, not a keyboard.
3. **Choose, don't type.** Plants, suppliers, greenhouses, pot sizes and loss reasons are drop-down lists, with the most recently used first.
4. **One press to do it again.** Anything recorded before can be repeated with a single press, then adjusted if needed. Every screen offers a short list of recent actions.
5. **Nothing is overwritten.** Every change is a permanent event with who and when.
6. **Records are a by-product of work.** Nobody fills in compliance paperwork separately.
7. **Suggestions need a person.** Outside data is always confirmed before saving.

## Where it is used

Mainly large greenhouses, possibly a polytunnel later.

- **Bench station:** a tablet or inexpensive PC in the potting area, with a USB or Bluetooth barcode scanner and the label printer. This is the primary input point, because wet hands, gloves, glare and condensation make touchscreens and phone cameras unreliable.
- **Walk-round device:** a phone, using an installable web app with camera scanning. A native iPhone app follows once the flows are proven. Offline capture queues events using client-generated identifiers, as a safety net rather than a headline feature. Design and test for iPhone Safari first, assuming queued events send only while the app is open, so the screen must show what is still waiting. Work waits per person, so it is only ever sent under the name of whoever recorded it. A device keeps recent container details so labels can still be opened. New batches and potting on need a connection, because they create new labels.

Labels must survive wet conditions: polypropylene or polyester stock with resin thermal transfer, or laser-printed plastic tags. Direct thermal labels fade and peel. Printer and stock must be chosen before stage 1 ships.

## Core concepts

- **Operator:** the nursery itself. Registration number for passport Part B, site addresses, authorisation renewal date, and VAT status.
- **Plant:** catalogue entry. Common name, variety, botanical name, category, position, watering, hardiness, Xylella host flag, protected-variety flag, photo, default price per pot size.
- **Supplier:** name, contact details, address, account or order reference, what they supply, typical lead time, notes, passport registration number where relevant, and price and purchase history.
- **Purchase:** a record of buying from a supplier: date, lines, cost, VAT, paid or unpaid, and a photo of the receipt. Plant lines can create a batch and its received-passport record.
- **Location:** site, greenhouse or polytunnel, bench or bay.
- **Batch:** lineage only. One plant raised together, with a traceability code, source (seed lot, bought-in plants or cuttings), parent batch or mother plant, and original origin country.
- **Container:** the physical unit: tray, pot group or bench lot. It has a label, location, pot size, count, and the batch or batches it holds. A mixed tray is one container with several batch lines.
- **Event:** sown, received (bought-in plants), cuttings taken, germinated, pricked out, potted on, moved, lost, split, treated, examined, sold, stock counted, corrected, note. May carry a photo.
- **Received passport:** the parts copied exactly from the supplier's label, with who supplied it, date received and status: held, used up or no longer valid. Part C may be absent on plants ready for final sale, and Part E is usually blank. What was printed can never be changed afterwards.
- **Consignment:** a sale or movement. Contains **packages,** the smallest unit transported, whose lines reference batches and quantities.
- **Issued passport:** belongs to a package. May list several species. Every Part C code must resolve to every batch in the package.
- **Sale, customer, task, treatment record:** described below.

Plants, suppliers, locations and pot sizes are archived rather than deleted, so older records keep their meaning, and can be brought back. A place can't be archived while trays or other places are in it. Names in these lists are unique regardless of capitals. Every change to them is kept with who made it and when, except that the values of personal contact details are not kept in that history, so they can be erased.

Partial potting-on creates a new container from the same batch, and no new batch. A split creates a new batch only when lineage truly forks, such as different treatment, destination or passporting.

## Main flows

1. **Start a batch.** Choose or add a plant, enter quantity, source and greenhouse, print a label.
2. **Record what happened.** Scan, choose an action, confirm, with an undo for the last action. Recent actions appear as one-press shortcuts, such as "another tray of Lavender 'Hidcote', Greenhouse 2".
3. **Record losses.** Scan, Lost, how many, why: failed to germinate, damping off, pest, disease, frost, other.
4. **Pot on.** Scan, how many, new pot size. Prints new container labels.
5. **Move.** Scan the container, scan or choose the bench.
6. **Count stock.** Scan, enter the count.
7. **Sell or dispatch.** Build a consignment; the system decides whether a passport is required.
8. **Print.** Plant labels, bench cards, container labels, passports, invoices.

### Corrections and counts

A **corrected** event references the event it reverses and carries the replacement. **Stock counted** records what was there when the count was made, and the difference from the stock at that moment is recorded as an **adjustment,** separate from losses in statistics. Work from before a count that reaches the server later, or is undone later, is absorbed by that count's adjustment, so the count still stands. A derived count can never go below zero: the interface asks for a correction or a stock count instead. A count can only name a batch that has been in that container.

The time a device gives for work is believed unless it is more than a few minutes ahead of the server, when the server's time is used. Work is sent with an identifier made on the device, so a resend is recognised; an identifier already used for different work is refused. Work that can't reach the server, including while it restarts, waits on the device and is sent later; work the server refuses is kept on the device so it can be tried again.

Work can be undone from Recent or from a container's history, which shows who did what and when; undone work stays in the history, marked as undone. Places are shown with the places they sit in, such as "Greenhouse 2, Bench A".

A container's location, pot size and contents are derived from its events. Location and pot size follow the order the work happened, so events arriving late from an offline device fall into place.

## Plant passports and plant health

Source: GOV.UK, "Issuing plant passports to trade plants in Great Britain", updated 14 October 2026, applying to England and Wales.

- Printed passport: "UK Plant Passport" at the top, parts A to E, separate from other label information, no flag.
- Part A botanical name (genus and species, or genus if the species is unknown). Part B operator registration number, no "GB" prefix. Part C traceability code. Part D country of origin as a two-letter ISO code. Part E only for Qualifying Northern Ireland Goods.
- The traceability code can be a serial, week, batch or invoice number and must trace to the unit where the passport was issued. A QR code cannot replace it.
- Required for sales to other professional operators and for distance sales, including to personal-use customers. Not required for in-person retail to personal-use customers.
- Attached to the smallest transport unit, and may list several species if traceability covers them. For distance sales it may be on the plant or container, the enclosed delivery note, or the outside of the package.
- **Origin is derived at issue time.** Plants are grown-on after potting, re-potting, grafting or rooting, or after 2 weeks in active growth under protection or 4 weeks outside, and origin then changes. Certain Xylella fastidiosa and Ceratocystis platani hosts may show GB only after one year in the UK. The issue screen shows the rule applied.
- Every passport issue is preceded by a recorded examination.
- A received passport cannot be reused once its batch has been split, repotted or grown on, or if traceability is lost or plants are not pest and disease free. A new passport must be issued.
- Sales of ornamental propagating material to professionals also need a supplier document, which cannot be replaced by a passport.
- Who supplied every received passport must be recorded. Issued passports are recorded for 3 years, with who they went to. GOV.UK sets no period for received passports, so GrowLog keeps them permanently. Records must allow APHA to investigate outbreaks.
- Authorisation is renewed yearly, with a chargeable inspection. The system reminds the owner.

The system validates botanical names before issue; retains records for at least 3 years and blocks earlier deletion; flags Xylella hosts, treating a plant nobody has checked as not checked rather than as not a host; warns when taking cuttings from a protected variety; records suspected quarantine pests and prompts a call to APHA; and produces inspection, trace-back and trace-forward reports.

## Other legal records

- **Treatments:** product, date, dose, area or crop treated, operator. Kept for at least 3 years, subject to confirmation against current HSE guidance.
- **Invoices:** simple trade invoices. With a VAT-registered switch producing the required VAT content, including a rate per line, because some plants, seeds and food plants differ.

## Personal data

Trade and distance-sale customer details are personal data under UK GDPR. Lawful bases: legal obligation for passport records, contract for customers. Customer contact details can be erased while the passport trace record is kept.

## Backups

Scheduled `pg_dump` to a second location, with a tested restore procedure, in stage 1. The promise to retain records means nothing without this.

## Production view

One screen lists everything growing, for all staff: each tray or pot group with its plant, quantity, place, pot size, current stage and how long it has been there. Tabs filter by stage (sown, cuttings, bought in, germinated, pricked out, potted on, ready) with live plant counts, alongside search and filters for place and supplier. The stage is worked out from recorded events, honouring undos and corrections. "Ready" is a one-press action on the container, recorded like any other event. Estimating when plants will come ready needs growing times per plant, which are not held yet.

## Statistics

Germination rate, survival by stage, losses by reason, and performance by variety, supplier, greenhouse and season, derived from events. Stock adjustments are reported separately.

## Data enrichment

Adding a plant or batch starts with a search showing the common name, scientific name and a picture. It checks the nursery's own plants first, then a local list of species saved from earlier searches, then iNaturalist, with GBIF as a fallback. Only pictures licensed CC0, CC BY or CC BY-SA are used, because a nursery is a business; the credit is stored and shown. Picking a species fills in the accepted botanical name from GBIF (CC BY 4.0, attribution stored and shown). Saved species are reference data until a person picks one. Picking one also gathers the family from GBIF, UK native status from iNaturalist, and a description and growing section from Wikipedia (CC BY-SA 4.0, credited). Position, watering and hardiness are suggested from simple matches in that text, and a person confirms them. Each plant has a card showing this alongside growing notes staff add, which are only ever added to. Pot sizes and places can have photos, and pot sizes a count of how many are held. Most care-data sources are proprietary or non-commercial, so they cannot be used unless properly licensed, and the RHS does not permit scraping. Source and licence are stored per field. No external service may be required for the system to work.

## Buying, selling and simple accounts

Users asked for simple record keeping for buying and selling, so this covers more than sales alone:

- **Money out:** purchases from suppliers, with receipt photos.
- **Money in:** sales from which batch, including a daily till total entered or imported from Square, plus trade invoices and whether they have been paid.
- **Summaries:** income and spending by month, and what is owed or owing.
- **Export:** CSV for an accountant or bookkeeper.

Purchases and daily till totals are not edited once recorded: a mistake is voided and entered again.

This is single-entry record keeping, not a full accounting package. It does not take payments, and it does not file tax returns or submit anything to HMRC. Square remains the record of card payments.

## Voice

Not a priority: users preferred drop-down lists and one-press repeats. If it is added later, recognition must be limited to the nursery's own plant and location names, and nothing saves without confirmation.

## Other features

Reminders, cuttings from mother plants, pest and disease notes with photos, reservations, availability lists, CSV import and export.

## Architecture

| Part | Choice |
| --- | --- |
| API | ASP.NET Core, .NET 10 LTS |
| Data | PostgreSQL with EF Core |
| Web | Svelte 5, static build served by the API, installable with camera scanning |
| iPhone | Native SwiftUI, after the web flows are proven |
| Contract | OpenAPI, generating web and Swift clients |
| Sign-in | ASP.NET Identity. The owner chooses per person: a passkey, a password, or just tapping their name, which is only suitable on a private network and is off until the owner turns it on for the nursery. The owner's account always needs a password or passkey. Anyone can then set their own password, PIN or passkey in settings. A shared staff login through OpenID Connect can be set up by the owner under Manage, with deployment settings as the starting point until they save; it is optional, GrowLog's own sign-in keeps working without it, and accounts are matched only by the provider's stable identifier, never by name. The owner signs a bench station in once, which needs at least one person with a PIN; staff then tap their name and enter a short PIN, and it returns to the name list after a few minutes idle. A PIN at a bench never carries the owner's powers, and a bench station can be turned back into an ordinary device |
| Printing | QRCoder and Typst, producing PDFs |
| Deployment | One application container plus PostgreSQL, using Docker Compose |

Events are the source of truth, and current counts are derived projections. The database itself refuses any update or delete of an event, so a mistake is put right with a new corrected event. Event details must not hold personal data, which has to stay erasable.

## Licence

AGPL-3.0-or-later. Dependencies and bundled fonts must use open-source licences compatible with it. Avoid packages that have moved to commercial or restricted terms.

## Delivery stages

1. **Records.** Sign-in, plants, suppliers, locations, batches, containers, events, losses, treatment records, drop-down lists and one-press repeats, phone camera scanning, offline capture, labels, received-passport records, basic germination and survival figures, backups.
2. **Buying and selling.** Purchases, sales, consignments, passport issue, invoices, simple accounts, reminders, trace reports. Trade sales follow once retail and online selling work.
3. **Extras.** Native iPhone app, enrichment beyond names, online shop stock synchronisation, and voice if still wanted.

## Open questions

- Label printer and stock.
- Whether the nursery's inspector accepts the received-passport status values, the status values for issued passports, and any further records they expect.
