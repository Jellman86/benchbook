// Goes on a tray or pot group, to scan when recording work. The code is printed in words as well,
// so the tray can still be found if the QR code is scratched or muddy.
#import "Shared.typ": *
#show: sheet.with(margin: 1.5mm)

#let start = calc.max(calc.min(height, width * 0.5) * choose(0.13, 0.16, 0.2), 8pt)
#let lines = item.lines

// Each level leaves out a little more: Latin names, then the nursery's name, then the logo, then
// plants from the end of a mixed tray. The last lets a very long word break.
#let levels = 4 + calc.max(lines.len() - 1, 0) + 1

#let draw(size, level) = layout(space => {
  set text(size: size, hyphenate: level == levels - 1)
  let small = calc.max(size * 0.6, smallest)
  let shown = calc.max(1, lines.len() - calc.max(level - 3, 0))
  let code = text(font: mono, weight: "bold", item.code)
  // The logo takes what the code leaves on its line, and steps aside if that is too little.
  let beside = calc.min(space.width * 0.35, space.width - measure(code).width - 1.5mm)
  let has-logo = style.showLogo and level < 3 and beside >= 5mm

  grid(
    columns: (1fr, auto),
    column-gutter: 1.5mm,
    align: (left + horizon, right + horizon),
    code,
    if has-logo { logo(beside, size * 1.1) },
  )
  v(size * 0.3)
  {
      for line in lines.slice(0, calc.min(shown, lines.len())) {
        block(above: 0.75em, below: 0.3em, text(size: calc.max(size * 0.85, 7pt), weight: "bold", line.plant))
        if style.showBotanical and level < 1 and line.botanical != "" {
          block(below: 0.3em, text(size: calc.max(size * 0.7, smallest), style: "italic", line.botanical))
        }
        block(text(size: small)[Batch #text(font: mono, size: 0.9em, line.batch)])
      }
      if shown < lines.len() {
        block(above: 0.5em, text(size: small, weight: "bold")[and #(lines.len() - shown) more])
      }
  }

  if style.showNurseryName and level < 2 {
    v(1fr)
    v(0.8mm)
    text(size: small, data.nursery)
  }
})

#with-qr(pad(
  right: 0.5mm,
  y: 0.5mm,
  fit(
    sizes(start, 7pt),
    levels,
    draw,
    fits: (size, level, space) => level == levels - 1 or lines.all(line =>
      words-fit(line.plant, space.width, word => text(size: calc.max(size * 0.85, 7pt), weight: "bold", word))
    ),
  ),
))
