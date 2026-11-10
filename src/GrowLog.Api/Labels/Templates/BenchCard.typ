// Stands on the sales bench and is read from a step away: the name, how to grow it, and the prices.
#import "Shared.typ": *
#show: sheet.with(margin: calc.clamp(calc.min(width, height).mm() * 0.07, 3, 8) * 1mm)

#let start = calc.min(width, height * 1.2) * choose(0.085, 0.11, 0.14)
#let has-botanical = style.showBotanical and item.botanical != ""
#let care = if style.showCare {
  (("Position", item.position), ("Watering", item.watering), ("Hardiness", item.hardiness)).filter(entry => entry.at(1) != "")
} else { () }
#let prices = if style.showPrice { item.prices } else { () }
#let has-header = style.showLogo or style.showNurseryName

// A wide card puts the prices beside the words; an upright one puts them along the bottom.
#let wide = width >= height * 1.6 and prices.len() > 0
#let beside = 0.4
#let gap = 3mm

// Each level leaves out a little more: care, then the nursery's name and logo. The last lets a
// very long word break.
#let levels = 4

// Prices are what a customer looks for, so they stay large however much else is on the card.
#let price-size(size) = calc.max(size * if prices.len() <= 2 { 0.95 } else if prices.len() <= 4 { 0.8 } else { 0.66 }, 11pt)
#let pot-size(size) = calc.max(price-size(size) * 0.7, 8pt)

#let draw(size, level) = layout(space => {
  set text(size: size, hyphenate: level == 3)
  let small = calc.max(size * 0.46, 7.5pt)

  let header = if has-header and level < 2 {
    grid(
      columns: (auto, 1fr),
      column-gutter: if style.showLogo and style.showNurseryName { 2.5mm } else { 0mm },
      align: horizon,
      if style.showLogo { logo(space.width * 0.3, calc.clamp(size.mm() * 1.1, 6, 16) * 1mm) },
      if style.showNurseryName { text(size: calc.max(size * 0.4, 7pt), weight: "semibold", data.nursery) },
    )
    v(1.2mm, weak: true)
    line(length: 100%, stroke: rule)
    v(calc.max(size * 0.5, 2mm), weak: true)
  }

  let title = {
    block(text(weight: "bold", item.plant))
    if has-botanical { block(text(size: calc.max(size * 0.6, 7.5pt), style: "italic", item.botanical)) }
  }

  let growing = if care.len() > 0 and level < 1 {
    v(size * 0.5, weak: true)
    set text(size: small)
    grid(
      columns: (auto, 1fr),
      column-gutter: 0.8em,
      row-gutter: 0.55em,
      ..care.map(entry => (text(weight: "bold", entry.at(0)), entry.at(1))).flatten(),
    )
  }

  let price-size = price-size(size)
  let price-list = grid(
    columns: (1fr, auto),
    column-gutter: 0.6em,
    row-gutter: 0.5em,
    align: (left + bottom, right + bottom),
    ..prices.map(entry => (
      text(size: pot-size(size), entry.size),
      text(size: price-size, weight: "bold", entry.price),
    )).flatten(),
  )

  if wide {
    grid(
      columns: (1fr, space.width * beside),
      column-gutter: gap,
      { header; title; growing },
      grid.vline(stroke: rule),
      block(height: 100%, inset: (left: gap), align(horizon, price-list)),
    )
  } else {
    header
    title
    growing
    if prices.len() > 0 {
      v(1fr)
      v(size * 0.4)
      line(length: 100%, stroke: rule)
      v(size * 0.3, weak: true)
      price-list
    }
  }
})

#fit(
  sizes(start, 9pt),
  levels,
  draw,
  fits: (size, level, space) => level == 3 or {
    let room = if wide { space.width * (1 - beside) - gap } else { space.width }
    let beside-room = if wide { space.width * beside - gap } else { space.width }
    // Beside every price there must still be room for the longest word of its pot size.
    (prices.all(entry => words-fit(
      entry.size,
      beside-room - measure(text(size: price-size(size), weight: "bold", entry.price)).width - pot-size(size) * 0.6,
      word => text(size: pot-size(size), word),
    ))
      and words-fit(item.plant, room, word => text(size: size, weight: "bold", word))
      and (not has-botanical or words-fit(item.botanical, room, word => text(size: calc.max(size * 0.6, 7.5pt), style: "italic", word))))
  },
)
