// Goes in or on the pot, for customers: the name first, the price second, everything else small.
#import "Shared.typ": *
#show: sheet.with(margin: if calc.min(width, height) < 40mm { 2mm } else { 3mm })

#let start = calc.min(height, width * 0.5) * choose(0.15, 0.19, 0.24)
#let has-botanical = style.showBotanical and item.botanical != ""
#let has-care = style.showCare and item.care != ""
#let has-price = style.showPrice and item.price != ""

// Each level leaves out a little more: care, then the nursery's name, then the logo. The last
// lets a very long word break.
#let levels = 5
#let logo-wide(space, level) = if style.showLogo and level < 3 { space * 0.3 } else { 0pt }

#let draw(size, level) = layout(space => {
  set text(size: size, hyphenate: level == 4)
  let small = calc.max(size * 0.6, smallest)
  let has-name = style.showNurseryName and level < 2
  let has-logo = style.showLogo and level < 3

  let words = {
    block(text(weight: "bold", item.plant))
    if has-botanical { block(text(size: calc.max(size * 0.8, smallest), style: "italic", item.botanical)) }
    if has-care and level < 1 { block(above: 0.7em, text(size: small, item.care)) }
  }

  // A price must be clear and of a reasonable size, so it never shrinks as far as the rest.
  let foot = if has-price or has-name {
    grid(
      columns: (1fr, auto),
      column-gutter: 2mm,
      align: (left + bottom, right + bottom),
      if has-name { text(size: small, data.nursery) },
      if has-price { text(size: calc.max(size * 1.2, 9pt), weight: "bold", item.price) },
    )
  }

  if has-logo {
    grid(
      columns: (1fr, auto),
      column-gutter: 2mm,
      words, logo(logo-wide(space.width, level), calc.min(height * 0.3, 14mm)),
    )
  } else {
    words
  }

  if foot != none {
    v(1fr)
    v(0.8mm)
    foot
  }
})

#fit(
  sizes(start, 7pt),
  levels,
  draw,
  fits: (size, level, space) => level == 4 or {
    let room = space.width - logo-wide(space.width, level) - if style.showLogo and level < 3 { 2mm } else { 0mm }
    (words-fit(item.plant, room, word => text(size: size, weight: "bold", word))
      and (not has-botanical or words-fit(item.botanical, room, word => text(size: calc.max(size * 0.8, smallest), style: "italic", word))))
  },
)
