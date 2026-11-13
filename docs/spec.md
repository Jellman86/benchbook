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
- **Walk-round device:** a phone, using an installable web app with camera scanning. A native iPhone app follows once the flows are proven. Offline capture queues events using client-generated identifiers, as a safety net rather than a headline feature. Design and test for iPhone Safari first, assuming queued events send only while the app is open, so the screen must show what is still waiting. Work waits per person, so it is only ever sent under the name of whoever recorded it. A reply lost while someone signs out stays in that person’s queue, and sending stops before another person signs in. Moving work between waiting and refused is saved in one step; a storage failure is shown so the person can keep the screen open and try again. A device keeps recent container details so labels can still be opened. New batches and potting on need a connection, because they create new labels.

Labels must survive wet conditions: polypropylene or polyester stock with resin thermal transfer, or laser-printed plastic tags. Direct thermal labels fade and peel. Printer and stock must be chosen before stage 1 ships. Labels are designed for a 300 dpi desktop thermal transfer printer, which prints about 104 mm wide at most, so the sizes offered are no wider than 100 mm: 50 × 25 mm for plant and tray labels and 100 × 150 mm for bench cards and place signs to begin with, so that two rolls cover everything.

The owner sets the nursery's name, address, registration and VAT numbers and logo once, under Settings, Nursery details, and designs labels under Settings, Labels, beside the nursery's printers under Settings, Printers. Each kind of label (plant label, bench card, tray label, place label) has a fixed, tested layout with a text size, a size in millimetres to match the label stock, and switches for what it shows: logo, nursery name, Latin name, price and care. A sample is drawn as the owner changes these, before saving, and the screen explains how to set the printer up: the label's size in the printer's settings, and printing at actual size. Any label can be printed as several copies at once.

Network label printers are added under Settings, Printers, by IP address or network name and port (9100 by default), with their language, ZPL (Zebra and compatible) or TSPL (TSC and the many compatible printers), their print head's resolution, and which kinds of label they print. A label of a kind a printer takes then prints straight to it with one press, as a single picture at the printer's resolution followed by the number of copies, and a PDF is always offered beside it. A test print sends a sample label. The server only sends to addresses a printer could have, never to any of its own network addresses or reserved addresses, and only on printer ports 9100–9102, 515 or 631, so it must be able to reach the printer over the network. Printing at the bench puts the scan box back in focus afterwards. A free-form label designer is not planned: fixed layouts can't clip or misprint, and the plant passport must stay separate.

Without a label printer, labels print on A4 sticker sheets from an ordinary printer, from Record's "Print labels on A4": choose trays, places or plants (by search, or all shown), a sheet (65, 24, 21, 14 or 8 to a page, matching the common makes), the first free sticker, by tapping it on a picture of the sheet or typing its number, so a part-used sheet is finished first, copies of each, and cut lines for plain paper. Each label is its usual design drawn to fit one sticker; the first sheet can be shown on screen before printing, drawing only that sheet's labels, and the labels open in a new tab to print at actual size, sent as an ordinary form so they open from an iPhone's home-screen app too. At most 300 labels at once; more says so. A label never clips or runs onto a second label. Long names are printed smaller, down to a size that still reads; after that the label leaves out, in order, the care notes, the nursery's name and the logo, and a mixed tray lists as many plants as fit and says how many more there are. A price is always printed in pounds including VAT, in bold, and never shrinks below a clearly legible size. Text is solid black and lines and QR squares are whole printer dots, because a thermal printer has no greys. Tray and place labels print their code in words beside the QR code. No design uses the words "Plant Passport" or lettered parts: an issued passport will be its own label, or its own bordered block, so it stays distinct from everything else.

## Core concepts

- **Operator:** the nursery itself. Registration number for passport Part B, site addresses, authorisation renewal date, and VAT status.
- **Plant:** catalogue entry. Common name, variety, botanical name, category, position, watering, hardiness, Xylella host flag, protected-variety flag, photo, default price per pot size.
- **Supplier:** name, contact details, address, account or order reference, what they supply, typical lead time, notes, passport registration number where relevant, other registration numbers (such as for seed marketing standards), and price and purchase history. Anyone recording work can add or change a supplier's registration numbers, from New batch or Edit batch; contact details stay the owner's.
- **Purchase:** a record of buying from a supplier: date, lines, cost, VAT, paid or unpaid, and a photo of the receipt. Plant lines can create a batch and its received-passport record.
- **Location:** a site, and any place inside it: greenhouse, polytunnel, bench, bay, standing bed, cold frame, sales area or area. Any place can sit inside any other, and can be moved to another. Each has a short code such as P0001 for a printed label, so moving a tray can be: scan the tray, scan the place.
- **Batch:** lineage only. One plant raised together, with a traceability code, source (seed lot, bought-in plants or cuttings), parent batch or mother plant, original origin country, and for seed the sow-by or use-by month from the packet.
- **Container:** the physical unit: tray, pot group or bench lot. It has a label, location, pot size, count, and the batch or batches it holds. A mixed tray is one container with several batch lines.
- **Event:** sown, received (bought-in plants), cuttings taken, germinated, pricked out, potted on, moved, lost, split, treated, examined, sold, stock counted, corrected, note. May carry a photo.
- **Received passport:** the parts copied exactly from the supplier's label, with who supplied it, date received and status: held, used up or no longer valid. Part C may be absent on plants ready for final sale, and Part E is usually blank. What was printed can never be changed afterwards.
- **Consignment:** a sale or movement. Contains **packages,** the smallest unit transported, whose lines reference batches and quantities.
- **Issued passport:** belongs to a package. May list several species. Every Part C code must resolve to every batch in the package. Issuing starts with one batch from one tray: Part C is the batch's code, and a package of several batches comes later.
- **Sale, customer, task, treatment record:** described below.

Plants, suppliers, locations and pot sizes are archived rather than deleted, so older records keep their meaning, and can be brought back. Hierarchy moves are serialised, and a place cannot be moved beneath itself or an ancestor whose full chain cannot be resolved. A place can't be archived while trays or other places are in it. Names in these lists are unique regardless of capitals. Every change to them is kept with who made it and when, except that the values of personal contact details are not kept in that history, so they can be erased.

Partial potting-on creates a new container from the same batch, and no new batch. A split creates a new batch only when lineage truly forks, such as different treatment, destination or passporting.

## Main flows

1. **Start a batch.** Choose or add a plant, enter quantity, source, the day it was sown, arrived or had cuttings taken (today unless changed; a batch started from a purchase takes the purchase date), and greenhouse, then check everything on one page, where each answer can be changed, before the batch is started and its label printed. Bought-in plants must name their supplier, for the passport record. Seed need not be counted as it goes in: New batch asks "How much seed went in?" and takes a count, "about" a number of seeds, a weight in grams, a whole, half or part packet with what the packet says, or "don't know". Anything but a count starts the tray "not counted yet": it shows and can be worked on, but has no number until someone counts it. Germinated offers "How many are up?", a count or best guess that becomes the tray's number, with "Not sure yet"; a guess is kept as a rough count (ticked by default), shown as "about" so many, makes the germination rate approximate, and pricking out or potting on from it still asks how many were taken and how many are left, which replaces the guess. For seed sown by weight, Germinated offers "Use about N" from what a gram of the same plant's seed gave at the first count of earlier batches sown by weight, to tap rather than filled in; Count asks for a number with none filled in; pricking out or potting on asks how many were taken and whether any are left, and those together are the first count, recorded just before the move; Lost offers "All of them" or "Some of them", and all of them ends the tray: it stands as a count of nought at its time, so a count timed before it that arrives late from a device that was offline can't bring the plants back. Losses by reason count trays lost altogether before anyone counted them as trays, once each, beside the plants. Production's stage totals count trays not counted yet beside the plants ("40 + 2 uncounted"). A supplier saved from an older cached app, which knows nothing of other registration numbers, keeps them. Work queued offline follows whatever count reached the server first: "all of them" takes every plant counted since, "some" is refused with a reason, and a prick-out's taken and left still stand as a count. A number is never made up: an uncounted sowing can't be given one by correction or by Edit batch, only by a count. For the figures, the first count is where an uncounted batch starts; germination is given against counted seed, or as an approximate rate against "about" a number once it has been counted, each batch capped at its own estimate, and not at all for seed sown by weight, by the packet or not known. A tray where all were lost as failing to germinate is a real nought.
2. **Record what happened.** Scan, choose an action, confirm, with an undo for the last action. Recent actions appear as one-press shortcuts, such as "another tray of Lavender 'Hidcote', Greenhouse 2". Repeating cuttings keeps the mother plant and her name. Saving a germination count is one piece of work: Undo reverses both the germination and its count, including while waiting on the device.
3. **Record losses.** Scan, Lost, how many, why: failed to germinate, damping off, pest, disease, frost, other.
4. **Pot on.** Scan, how many, new pot size. Prints new container labels.
5. **Move.** Scan the container, scan or choose the bench. To move many, choose or scan the place once, then scan tray after tray; a place's page can also move everything on it at once.
6. **Count stock.** Scan, enter the count.
7. **Sell or dispatch.** Build a consignment; the system decides whether a passport is required.
8. **Print.** Plant labels, bench cards, container labels, passports, invoices.

### Corrections and counts

A **corrected** event references the event it reverses and carries the replacement. **Stock counted** records what was there when the count was made, and the difference from the stock at that moment is recorded as an **adjustment,** separate from losses in statistics. Work from before a count that reaches the server later, or is undone later, is absorbed by that count's adjustment, so the count still stands. A derived count can never go below zero: the interface asks for a correction or a stock count instead. A count can only name a batch that has been in that container, including a count made while pricking out or potting on. These checks and deciding whether a tray is uncounted happen while its stock is held against other work. An unchanged resend is recognised before checking the stock again. Earlier versions kept some counts as differences alone; upgrades recover their original figures in a separate record, without changing any event.

Days are chosen with one tap for today or yesterday, or from a calendar that starts weeks on Monday and offers no day after today; work dated an earlier day is recorded at the same time of day on that day. The time a device gives for work is believed unless it is more than a few minutes ahead of the server, when the server's time is used. Work is sent with an identifier made on the device, so a resend is recognised; an identifier already used for different work is refused. Work that can't reach the server, including while it restarts, waits on the device and is sent later; work the server refuses is kept on the device so it can be tried again.

A batch's details can be corrected from its tray with Edit batch: the plant, the supplier, lot or reference, mother plant and country of origin; details that don't apply to how it started are cleared. How it started (seed, bought in or cuttings), the day it was sown, arrived or had cuttings taken, and how many it started with can be corrected only while nothing else has been recorded on the plants; the start is then undone and recorded again as the right kind of work, never altered. In the same way, seed recorded as counted can be recorded as weighed, a packet, about so many or not known instead, which leaves the tray to be counted, and seed measured one of those ways can be given the number counted; once other work is recorded, only a count gives it a number. Received passports keep what was printed on them, and batches split from it keep their own details; the check page says so. After that a stock count corrects the number. Every correction is checked on a page showing each value before and after, is kept with who made it and when, and shows on the batch's trace report. A sale is reversed only by a credit note, so an invoice and its plants always agree. Work on a tray is recorded for today unless "Recording for" on the tray is changed to another day, for work written up later; everything then recorded on that tray is dated that day, at the time of day it's entered, and the day is kept for that tray for half an hour or until it's changed back to today, so several pieces of work can be written up in a row. The day picker starts at the day the tray was started; work dated before it is refused, but work on the start day itself timed earlier than the start is placed just after it, before any later observation and within the chosen day; if there is no room, it is refused. A start replaced by a correction keeps the original kind of work and time. Work dated before a later count of the tray leaves the number as counted, and a loss, pricking out or potting on says so. Choosing a day shows a calendar with month and year lists, so a date far off is a couple of taps. A tray with no label yet can be opened from Record by typing part of its plant, place or code into "No label? Find a tray"; matching trays show what is in them and where, and this works without a signal from what the device last saw. Work can be undone from Recent or from a container's history, which shows who did what and when; undone work stays in the history, marked as undone. Places are shown with the places they sit in, such as "Greenhouse 2, Bench A".

A container's location, pot size and contents are derived from its events. Location and pot size follow the order the work happened, so events arriving late from an offline device fall into place.

## Plant passports and plant health

Source: GOV.UK, "Issuing plant passports to trade plants in Great Britain", updated 14 October 2026, applying to England and Wales.

- Printed passport: "UK Plant Passport" at the top, parts A to E, separate from other label information, no flag.
- Part A botanical name (genus and species, or genus followed by “sp.” if the species is unknown). Part B operator registration number, no "GB" prefix. Part C traceability code. Part D country of origin as a two-letter ISO code. The letter E is printed with a blank value on a standard passport; GB(NI) is reserved for replaced Qualifying Northern Ireland Goods passports, which GrowLog does not issue.
- The traceability code can be a serial, week, batch or invoice number and must trace to the unit where the passport was issued. A QR code cannot replace it.
- Required for sales to other professional operators and for distance sales, including to personal-use customers. Not required for in-person retail to personal-use customers.
- Attached to the smallest transport unit, and may list several species if traceability covers them. For distance sales it may be on the plant or container, the enclosed delivery note, or the outside of the package.
- **Origin is derived at issue time.** Plants are grown-on after potting, re-potting, grafting or rooting, or after 2 weeks in active growth under protection or 4 weeks outside, and origin then changes. Certain Xylella fastidiosa and Ceratocystis platani hosts may show GB only after one year in the UK. The issue screen shows the rule applied.
- Every passport issue is preceded by a recorded examination.
- A received passport cannot be reused once its batch has been split, repotted or grown on, or if traceability is lost or plants are not pest and disease free. A new passport must be issued.
- Movements of ornamental propagating material, including seeds, also need a supplier document, which cannot be replaced by a passport.
- Who supplied every received passport must be recorded. Issued passports are recorded for 3 years, with who they went to. GOV.UK sets no period for received passports, so GrowLog keeps them permanently. Records must allow APHA to investigate outbreaks.
- Authorisation is renewed yearly, with a chargeable inspection. The system reminds the owner.

Passports are issued from a tray's Plant passports. GrowLog fills in Part A from the plant's botanical name, Part B from the nursery's registration number without "GB", Part C from the batch code and Part D by the rules above, and shows the rule it applied; it refuses while the botanical name or registration number is missing. The person says how many plants the passport covers, up to those in the tray, and who they go to, and ticks that they examined the plants just now; the examination is recorded on the tray with their name and can't be undone while the passport stands. An issued passport is kept unchanged, prints as its own label ("UK Plant Passport" and parts A to E in a border, with E blank), and shows on the batch's trace report with who it went to. Received and issued passports are shown on screen the way they print. For Part D, bought-in plants take GB once the plants in that tray were potted on, pricked out or split into it here, after two weeks without a break in a greenhouse, polytunnel or cold frame, or after four weeks without a break outside. Where only a time rule would give GB, the person must confirm active growth for that time. Without that confirmation the original country stays. The confirmation is kept with the issued passport. Xylella hosts, plants nobody has checked for Xylella, and plane trees (hosts of Ceratocystis platani) take GB only after a year here. The device names each passport, so a retry after a lost reply never issues a second, and the issue is refused if Part D has changed since the screen showed it. Who a passport went to is shown to the owner and to whoever issued it.

Plants going out together are recorded as a consignment under Manage, Consignments; only the owner sees them, because they name customers. A consignment says how the plants are going (to a business, posted or couriered, sold in person, or to the nursery's other site), who to (a customer or a name), the day sent and any notes, and packs plants into one or more packages, each the smallest unit transported: from counted trays, or plants already sold on an invoice not cancelled by a credit note, up to the number invoiced less any already in a consignment. GrowLog says whether a passport is needed and why: yes to a business or at a distance, no in person or to the nursery's own site. A tray can't give more across the packages than it holds. Each package that needs a passport gets one, after the person ticks that they examined its plants just now: one examination recorded on each tray in it, behind every batch from that tray, and one issued passport line for each batch, belonging to the package. At issue, all affected trays are held in a consistent order and the quantities for each batch in each tray are checked together; an invoice must still be eligible and not credited. The package's passport gives Part B and a blank E once, then a row for each batch with its own botanical name (A), traceability code (C) and origin (D) together, so each code and origin stays with its plant; it prints on its own 100 × 150 mm page, carrying on over more pages with the heading, Part B and a blank E on each, and each line shows on its batch's trace report. Issuing it again finds the one issued only if the request is unchanged; a reused identifier with different content is refused. Recording a consignment doesn't change what's in the trays; an invoice or a loss does that, so plants already invoiced are offered from their invoices, and their passport is worked out from the tray they left. It can be dated up to a fortnight ahead, is named by the device so a retry never makes a second, and can be removed while none of its passports has been issued.

Before issue, Part A must contain at least two words (genus and species, or genus and “sp.”). This is a structural check: it cannot establish that the words are botanical rather than common names. Part D must be a real ISO 3166-1 alpha-2 country code, including GB. Passport printing refuses with “The label stock is too small for this passport” when mandatory content cannot fit; it never clips it. The system retains records for at least 3 years and blocks earlier deletion; flags Xylella hosts, treating a plant nobody has checked as not checked rather than as not a host; warns when taking cuttings from a protected variety; records suspected quarantine pests and prompts a call to APHA; and produces inspection, trace-back and trace-forward reports.

Consignment and passport issue screens remind the person that ornamental propagating material needs a separate supplier document. Seed and cutting batches before potting on or marking ready, and pot sizes named as plugs, liners, seed, cuttings or rootstocks, trigger the batch reminder. Preparing that document remains to come.

A batch's trace report is under Manage, Trace a batch, and from "Trace this batch" on a tray; only the owner sees it, because it names customers. A batch is found by its code, by any tray or pot it has been in, or by the plant's name. The report gives where the batch came from (how it started, seed lot or reference, mother plant, country of origin, supplier with their registration number, the purchase, and the batch it was grown from), every passport received with it and its status, whether the plant is a Xylella host, how many are still growing and where, how many were lost and sold, who bought them, by invoice, with any cancelled by a credit note, the batches split from it, and everything done to it oldest first: sowing, moves into new containers, losses with their reason, treatments with product, dose and area, moves between places, counts and notes, each with who recorded it and when. Work done to a whole container counts for a batch while the batch was in it. Undone work is left out, and corrected work is shown as corrected. The report prints from the browser.

## Other legal records

- **Treatments:** product, date, dose, area or crop treated, operator. Kept for at least 3 years, subject to confirmation against current HSE guidance.
- **Invoices:** simple trade invoices. With a VAT-registered switch producing the required VAT content, including a rate per line, because some plants, seeds and food plants differ.

## Personal data

Trade and distance-sale customer details are personal data under UK GDPR. Lawful bases: legal obligation for passport records, contract for customers. Customer contact details can be erased while the passport trace record is kept.

## Backups

If backups have never reported, Home warns the owner after a 24-hour grace period from the first account’s creation. Before then an unreported backup is treated as still being set up.

Scheduled `pg_dump` to a second location, with a tested restore procedure, in stage 1. The promise to retain records means nothing without this.

## Settings

The sidebar holds everyday work: recording, new batches, production, and for the owner, Growing (plants, places) and Business (figures, money, invoices, customers, purchases, suppliers). What is set once and then left alone is under Settings, grouped as:

- **You:** my account, with appearance, password, passkey and bench PIN. Everyone has this.
- **The nursery:** nursery details and logo, features, map (the satellite picture key), pot sizes, sensors. Owner only.
- **Printing:** printers, and labels (each kind's design and size). Owner only.
- **People:** staff, signing in (how much signing in, and the OpenID Connect staff login), bench stations. Owner only.
- **Records:** data and backups, with when the last backup was made and copied away, and example data and starting afresh while those are switched on. Owner only.
- **GrowLog:** about GrowLog, with the version, the commit it was built from, what it runs on, its licence and a link to the exact source. Everyone has this, because the GNU AGPL entitles everyone using GrowLog over the network to its source.

On a desk the settings list stays beside the page; on a phone it's a list, and each page has Back.

Under Settings, Features, the owner turns off parts of GrowLog the nursery doesn't use, so nobody is shown them: sensors and climate, the places map, plant passports (with received passports, consignments and the authorisation renewal reminder), mother plants, jobs, trade invoices, purchases and batch photos. Each is on until turned off, and the page says what turning it off hides. Off hides it everywhere for everyone: its Manage and Settings pages, its parts of other screens (passports and photos on trays, choosing a mother in New batch, jobs due on Record, owed to suppliers, invoiced plants in Consignments) and its reminders under "Needs a look"; the server refuses its requests too, so a sensor's readings are turned away while climate is off. Nothing recorded is deleted, and turning a part back on brings it all back. Customers stay while either invoices or passports are on. A device keeps the last switches it saw, so the app looks the same without a signal. Plants, places, batches, work, labels, trace, figures, money and backups are always on.

## Bringing records in and taking them out

Under Settings, Data and backups, the owner can download every record at once: a zip of spreadsheet (CSV) files, one each for plants, suppliers, customers, places, batches, what each tray holds now, every piece of work (one row for each tray and batch it touched, with who recorded it and whether it was undone), purchases, invoices, till takings, plant passports issued and received, consignments, jobs and mother plants. The download is a single database snapshot, including count adjustments with references to the work and count they belong to, and the effective time of each work line. Text a spreadsheet would run as a formula is kept as text; a phone number such as +44 is left as it is. Photos stay in the backups.

Plants, suppliers and customers can be brought in from a spreadsheet saved as CSV (comma, semicolon or tab separated, in UTF-8 or the Windows encoding Excel uses for an ordinary CSV). The browser reads the file and matches its columns to GrowLog's by name or a usual other name ("Latin name" for botanical name, say); the owner checks the matches, with columns not found waiting behind "Match more columns". Every row is then checked, and each problem named by its spreadsheet row: an empty name, text too long, a yes-or-no column with something else in it, a number out of range, or the same entry twice. Nothing is saved while any row has a problem; the owner puts the spreadsheet right or leaves those rows out. Something already recorded (a plant by its name and variety, a supplier or customer by name) is left as it is, so the same spreadsheet can be brought in twice. Up to 2,000 rows go in at once, all together or not at all.

## Climate

Temperature and humidity sensors send readings in, and Manage, Climate shows them for each place. The owner adds a sensor under Settings, Sensors, with a name and the place it's in; GrowLog shows its key once, with what to put in Home Assistant to send a reading every five minutes. Anything that can send a web request can send readings: a POST to /api/sensor-readings with the key as a bearer token and the temperature (°C) and humidity (%), optionally with when it was taken in the last day. Only a hash of the key is kept, a sensor sending more than once a minute is refused, and readings out of range (−50 to 80°C, 0 to 100%) are refused. A new key stops the old one working; a sensor no longer used keeps its readings and gives up its name for another. Two readings sent at the same moment can't both pass the once-a-minute limit. Readings are kept for two years, enough to compare a season with last year's. Moving a sensor counts its later readings for the new place.

Climate lists each place with its latest temperature and humidity, today's lowest and highest, and how long ago it was read, warning when nothing has been heard for over an hour. Opening a place charts temperature and humidity separately for today, 7 or 31 days: a line for each hour's average, shading from its lowest to highest, and a gap where nothing was heard. Dragging or hovering along a chart reads any hour, and a day-by-day table gives the same figures.

## Starting afresh

While a deployment has example data switched on (`Demo:Enabled`), the owner can load example data and clear every record from Settings, Data and backups, to start afresh, after typing "clear everything". It empties plants, batches, trays, places, pot sizes, suppliers, purchases, till takings, plant passports, consignments, jobs, mother plants, sensors and their readings, customers and invoices, restarts the label numbering, and removes only made-up example people that also match the demo’s archived, credential-free markers, never an owner or a person with credentials. Sign-ins, staff, bench devices, the nursery's details, logo and label designs are kept. Once example data is switched off for real records, records can no longer be cleared, because passport records must be kept for three years.

## Production view

Climate readings and the “last heard” clock refresh every three minutes and when the app becomes visible again. A slower response for a previously selected climate place, trace batch or received-passport batch cannot replace the current selection. Changing a customer on a phone opens and focuses the form. Sensor rate limits use server receive time under the sensor lock, at most one new reading a minute; repeated sample times are deduplicated separately.

One screen lists everything growing, for all staff: each tray or pot group with its plant, quantity, place, pot size, current stage and how long it has been there. Tabs filter by stage (sown, cuttings, bought in, germinated, pricked out, potted on, ready) with live plant counts, alongside search and filters for place and supplier. The stage is worked out from recorded events, honouring undos and corrections. "Ready" is a one-press action on the container, recorded like any other event. Estimating when plants will come ready needs growing times per plant, which are not held yet. "Choose several" lets anyone tick trays in that list (one by one, or all shown after filtering by stage, place, supplier or search) and then mark them ready, move them to a place, record a treatment on them, or print their labels on A4. Each tray is recorded on its own, so each waits on the device without a signal, and one refused doesn't stop the others; the result says how many were done, how many are waiting and names those that couldn't be, which alone stay chosen, so pressing again can't repeat the work on trays that took it.

## Statistics

Germination rate, survival by stage, losses by reason, and performance by variety, supplier, greenhouse and season, derived from events. Stock adjustments are reported separately.

## Data enrichment

Adding a plant or batch starts with a search showing the common name, scientific name and a picture. It checks the nursery's own plants first, then a local list of species saved from earlier searches, then iNaturalist, with GBIF as a fallback. Only pictures licensed CC0, CC BY or CC BY-SA are used, because a nursery is a business; the credit is stored and shown. Picking a species fills in the accepted botanical name from GBIF (CC BY 4.0, attribution stored and shown). Saved species are reference data until a person picks one. Picking one also gathers the family from GBIF, UK native status from iNaturalist, and a description and growing section from Wikipedia (CC BY-SA 4.0, credited). Position, watering and hardiness are suggested from simple matches in that text, and a person confirms them. Each plant has a card showing this alongside growing notes staff add, which are only ever added to. Pot sizes and places can have photos, and pot sizes a count of how many are held. Places can also be drawn on a map under Places, Map: the owner picks a place and taps its corners over a satellite picture, at any level from the site down to a bench, and taps an outline to open that place. Map names are plain text, including anything resembling HTML. Satellite pictures come from Esri when the owner adds a free ArcGIS Location Platform key under Settings, Map; without one the map is OpenStreetMap's street map. Finding the nursery by postcode asks postcodes.io (open data). The map is optional and only the owner sees it; the browser fetches map pictures directly, and nothing else depends on them. Most care-data sources are proprietary or non-commercial, so they cannot be used unless properly licensed, and the RHS does not permit scraping. Source and licence are stored per field. No external service may be required for the system to work.

## Buying, selling and simple accounts

Users asked for simple record keeping for buying and selling, so this covers more than sales alone:

- **Money out:** purchases from suppliers, with receipt photos.
- **Money in:** sales from which batch, including a daily till total entered or imported from Square, plus trade invoices and whether they have been paid.
- **Summaries:** income and spending by month, and what is owed or owing.
- **Export:** CSV for an accountant or bookkeeper.

Invoice and purchase creation can be named by the device, as consignments are, so an unchanged retry finds the record already made. New batches and transfers keep their original request across retries too. A reused identifier with different content is refused. Calendar days throughout the app use the device’s local date, including around midnight.

Purchases and daily till totals are not edited once recorded: a mistake is voided and entered again.

Each purchase line is either plants, which can become batches, or sundries such as compost and pots. A supplier's PDF invoice can fill in the purchase: its reference, date and item lines are read from the PDF's text, on the server and sent nowhere, and a person checks and completes them before saving. Photos and scans can't be read this way.

Trade customers are kept by the owner; their address, email and phone can be forgotten on request. Trade invoices are numbered without gaps and never changed once issued: a mistake is cancelled with a credit note. An invoice keeps the customer's name and address and the nursery's name, address, contact details and VAT number as they were at issue. Old invoices without a nursery snapshot use the current nursery details. Catalogue prices include VAT; when registered, an invoice prefills the net price by removing that line’s VAT rate and rounding to the penny. Changing the rate updates that prefill until the person edits the price; when not registered the catalogue price is copied as it stands. When the nursery is registered for VAT each line carries a rate (20, 5 or 0 per cent) and VAT is shown for each rate; otherwise the invoice says no VAT is charged. A line can take plants from a tray, which records them as sold; a credit note puts them back. Invoices print as PDFs with the nursery's details and logo, count as income when issued, and show as owed until marked paid.

This is single-entry record keeping, not a full accounting package. It does not take payments, and it does not file tax returns or submit anything to HMRC. Square remains the record of card payments.

## Voice

Not a priority: users preferred drop-down lists and one-press repeats. If it is added later, recognition must be limited to the nursery's own plant and location names, and nothing saves without confirmation.

## Other features

Repeating jobs, such as watering, feeding and spraying rotas, are set up by the owner under Manage, Jobs: what, every so many days or weeks, the next date, optionally a place, and notes. Jobs due or late show on Record with their notes, and anyone ticks one off; it moves on by its interval to the next date after today, never leaving a backlog and never earlier than it was, and keeps who did it and when. A tick made by mistake can be undone for a quarter of an hour, by whoever made it or the owner. Done twice on one day counts once. The owner also sees "Needs a look" on Record: the plant health authorisation renewal (from six weeks before the date set in Nursery details), overdue invoices, trays ready for over four weeks, trays sown without a count and not counted for three weeks, and jobs more than two days late. Each says why it is there, leads to where it's dealt with, and is worked out afresh, so it goes once it's dealt with. Mother plants are kept as records under Manage, Mother plants: which plant, a name, where it is and notes. Anyone taking cuttings can add one from New batch. Cuttings of a plant already on the list are taken from one of its mother plants, and each batch from a mother takes its next number (cuttings 1, 2, 3) under a lock, so two at once can't share one; the mother lists every batch taken from it, and the trace report says which mother and which batch. Cuttings taken from a mother stay hers: Edit batch can't change their source, mother or plant. A new mother's name typed in New batch is added on Next, even if Add wasn't tapped. The owner can mark a mother no longer used, and its cuttings stay recorded; her name can then be given to a new mother. Each batch has a strip of photos, oldest first, on its trays, Edit batch and the trace report. Anyone can add a photo of the plants from a tray; it is captioned with the day of the batch (the day it was sown or arrived is day 1) and the stage the tray had reached. Seed packet, label and packaging photos are added from Edit batch. Tapping a photo shows it large, and a wrong one can be removed for good by whoever took it or by the owner. Still to come: supplier documents for ornamental propagating material, pest and disease notes with photos, reservations, availability lists, CSV import and export.

## Architecture

| Part | Choice |
| --- | --- |
| API | ASP.NET Core, .NET 10 LTS |
| Data | PostgreSQL with EF Core |
| Web | Svelte 5, static build served by the API, installable with camera scanning |
| iPhone | Native SwiftUI, after the web flows are proven |
| Contract | OpenAPI, generating web and Swift clients |
| Sign-in | ASP.NET Identity. The owner chooses how much signing in the nursery wants: none, where devices go straight in and work is recorded as the nursery team; tap your name, for everyone including at a bench; each person's own way, below; or strict, with no tapping a name and a PIN at a bench. Sessions record how the person signed in; tightening the level ends name-tap sessions even for people who also have credentials. The owner's own account always needs a password or passkey. At "each person's own way" the owner chooses per person: a passkey, a password, or just tapping their name, which is only suitable on a private network. The owner's account always needs a password or passkey. Anyone can then set their own password, PIN or passkey in settings. A shared staff login through OpenID Connect can be set up by the owner under Settings, Signing in, with deployment settings as the starting point until they save; it is optional, GrowLog's own sign-in keeps working without it, and accounts are matched only by issuer and subject together, never by name. Changing authority clears its associations even while disabled; existing associations that predate issuer binding must be linked again from the person’s own signed-in account. The owner signs a bench station in once, which needs at least one person with a PIN; staff then tap their name and enter a short PIN, and it returns to the name list after a few minutes idle. A PIN at a bench never carries the owner's powers, and a bench station can be turned back into an ordinary device |
| Printing | QRCoder and Typst, producing PDFs, or pictures turned into ZPL or TSPL for network label printers |
| Deployment | One application container plus PostgreSQL, using Docker Compose |

Events are the source of truth, and current counts are derived projections. Readiness at `/api/health` checks the database with a short timeout and returns 503 when it is unreachable; `/api/health/live` answers independently of the database. Container health and browser-test startup use readiness. Printing has one application-wide limit on Typst processes, and identical labels within a sheet job are drawn once. `Printing:AllowLocal` is a test-only exception for pretend printers on loopback and ephemeral ports; normal printing remains restricted to the listed printer ports.

The database itself refuses any update or delete of an event, so a mistake is put right with a new corrected event. Event details must not hold personal data, which has to stay erasable.

## Licence

AGPL-3.0-or-later. Dependencies and bundled fonts must use open-source licences compatible with it. Avoid packages that have moved to commercial or restricted terms.

## Delivery stages

1. **Records.** Sign-in, plants, suppliers, locations, batches, containers, events, losses, treatment records, drop-down lists and one-press repeats, phone camera scanning, offline capture, labels, received-passport records, basic germination and survival figures, backups.
2. **Buying and selling.** Purchases, sales, consignments, passport issue, invoices, simple accounts, reminders, trace reports. Trade sales follow once retail and online selling work.
3. **Extras.** Native iPhone app, enrichment beyond names, online shop stock synchronisation, and voice if still wanted.

## Roadmap

**Done so far.** Stage 1 in full. From stage 2: purchases, invoices and credit notes, simple accounts, received and issued plant passports, consignments with a passport for each package, and trace reports. Alongside them: correcting batches, sowing seed without a count, finding a tray without a label, supplier registration numbers, seed packet dates, labels on A4 sticker sheets, dates on all work, work on many trays at once, repeating jobs with alerts, mother plants, a photo strip for each batch, greenhouse sensors, bringing records in from spreadsheets and taking everything out, and the smaller follow-ups from testing (rough counts, seedlings per gram, uncounted trays in the totals, lost trays staying lost).

**Next, in order:**

1. **Spray and sale warnings.** An alert when a tray treated with a product is marked ready or sold within the product's harvest or re-entry interval, once products carry one.

Stage 3 stays as above.

## Open questions

- A new name: "GrowLog" is already in use by another product, a grow log for home growers at growlog.space.
- Label printer and stock.
- Whether the nursery's inspector accepts the received-passport status values, the status values for issued passports, and any further records they expect.
