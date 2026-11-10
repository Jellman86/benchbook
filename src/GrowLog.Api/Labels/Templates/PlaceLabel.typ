// Goes on a bench or bed, to scan when moving trays: the place's name large enough to read from
// the path, where it is, and its code in words in case the QR code is damaged.
#import "Shared.typ": *
#show: sheet.with(margin: 1.5mm)

// An upright label is a sign for the end of a bench, so its name is sized from the width.
#let upright = height > width * 1.2
#let start = calc.max(if upright { width * choose(0.1, 0.13, 0.17) } else { calc.min(height, width * 0.5) * choose(0.16, 0.21, 0.28) }, 9pt)
#let has-path = item.path != item.name
#let has-foot = style.showLogo or style.showNurseryName

// Each level leaves out a little more: the nursery's name and logo, then where the place is. The
// last lets a very long word break.
#let levels = 4

#let draw(size, level) = layout(space => {
  set text(size: size, hyphenate: level == 3)
  let small = calc.max(size * 0.5, smallest)

  block(text(weight: "bold", item.name))
  if has-path and level < 2 { block(text(size: calc.max(size * 0.58, smallest), item.path)) }
  block(above: 0.7em, text(size: calc.max(size * 0.62, 8pt), font: mono, weight: "bold", item.code))

  if has-foot and level < 1 {
    v(1fr)
    v(0.8mm)
    grid(
      columns: (auto, 1fr),
      column-gutter: if style.showLogo and style.showNurseryName { 1.5mm } else { 0mm },
      align: horizon,
      if style.showLogo { logo(space.width * 0.4, calc.max(size * 0.9, 4mm)) },
      if style.showNurseryName { text(size: small, data.nursery) },
    )
  }
})

#with-qr(pad(
  right: 0.5mm,
  y: 0.5mm,
  fit(
    sizes(start, 8pt),
    levels,
    draw,
    fits: (size, level, space) => level == 3 or words-fit(item.name, space.width, word => text(size: size, weight: "bold", word)),
  ),
))
