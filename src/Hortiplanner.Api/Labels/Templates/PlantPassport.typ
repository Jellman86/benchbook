// A UK plant passport, as issued: "UK Plant Passport" at the top and parts A to E, inside its own
// border so it stays separate from anything else on the plants. No flag, as GOV.UK asks. Every part
// arrives in data.json and is only ever placed as text.
#import "Shared.typ": *
#show: sheet.with(margin: 1mm)

#let start = calc.max(calc.min(height * 0.2, width * 0.1), 7pt)
#let parts = (
  ("A", item.partA, true),
  ("B", item.partB, false),
  ("C", item.partC, false),
  ("D", item.partD, false),
  ("E", "", false),
)

// Only one level: a passport never leaves a part out, so the text shrinks instead, and a very long
// botanical name may break across lines.
#let draw(size, level) = {
  set text(size: size, hyphenate: false)
  let letter(it) = box(width: size * 1.3, text(weight: "bold", font: mono, it))

  block(below: size * 0.45, text(weight: "bold", size: size * 1.05)[UK Plant Passport])
  for (key, value, botanical) in parts {
    block(
      above: size * 0.28,
      grid(
        columns: (auto, 1fr),
        letter(key),
        if botanical { text(style: "italic", value) } else { text(font: mono, value) },
      ),
    )
  }
}

#block(
  width: 100%,
  height: 100%,
  stroke: rule,
  inset: snap(1.2mm),
  fit(sizes(start, smallest), 1, draw, required: true, fits: (size, level, space) => {
    let room = space.width - size * 1.3
    measure(text(weight: "bold", size: size * 1.05)[UK Plant Passport]).width <= space.width and parts.all(part => words-fit(part.at(1), room, words => if part.at(2) { text(style: "italic", size: size, words) } else { text(font: mono, size: size, words) }))
  }),
)
