// What every label shares. Typst code lives only in this file and the four designs. Everything a
// person typed arrives in data.json and is only ever placed as text, so none of it can run.

#let data = json("data.json")
#let item = data.item
#let style = data.style
#let width = float(sys.inputs.width) * 1mm
#let height = float(sys.inputs.height) * 1mm

#let serif = "Libertinus Serif"
#let mono = "DejaVu Sans Mono"

// One dot of a 300 dpi print head. Lines and QR squares are made from whole dots, so the printer
// never has to round a thin line away or smear the edge of a square.
#let dot = 25.4mm / 300
#let snap(length) = calc.max(1, calc.round(length / dot)) * dot
#let rule = 4 * dot

// A thermal head prints solid black only, and small type breaks up below about this size.
#let smallest = 6pt

#let choose(compact, standard, large) = (Compact: compact, Standard: standard, Large: large).at(style.layout)

/// The text sizes to try, from the one the owner chose down to the smallest that still reads.
#let sizes(from, to) = {
  let all = ()
  let size = from
  while size > to {
    all.push(size)
    size = size * 0.94
  }
  all + (to,)
}

/// Whether no single word is wider than the space, since a word can't be broken across lines.
/// A hyphen or a slash is somewhere a line can already break.
#let words-fit(words, space, dress) = words.split(regex("[\\s\\-–—/]+")).all(word => measure(dress(word)).width <= space)

/// Draws the fullest, largest version of a design that fits the space it is given. `body` takes a
/// text size and a level: each level leaves a little more out, and text is shrunk before anything
/// is left out. So a long name makes smaller text, never a clipped or overflowing label.
///
/// A design pushes its last part to the bottom with `v(1fr)`, which takes no room while measuring.
#let fit(sizes, levels, body, fits: (size, level, space) => true, required: false) = layout(space => {
  // A line's box ends at its baseline, so leave room for the tails of letters on the last line.
  let room = space.height - 2pt
  let chosen = (sizes.last(), levels - 1)
  let found = false

  for level in range(levels) {
    for size in sizes {
      let made = measure(body(size, level), width: space.width)
      if made.height <= room and fits(size, level, space) {
        chosen = (size, level)
        found = true
        break
      }
    }
    if found { break }
  }

  if required and not found { panic("The label stock is too small for this passport. Choose a larger label.") }
  block(width: space.width, height: space.height, clip: not required, block(width: 100%, height: room, body(..chosen)))
})

/// The nursery's logo, as large as fits inside the given box whatever its shape.
#let logo(most-wide, most-high) = context {
  let natural = measure(image(data.logo, height: most-high))
  if natural.width > most-wide {
    image(data.logo, width: most-wide)
  } else {
    image(data.logo, height: most-high)
  }
}

/// The QR code beside the words, or above them on an upright label. Its squares are a whole number
/// of printer dots, and it brings its own white border, which a scanner needs.
#let with-qr(body) = layout(space => {
  let upright = space.height > space.width * 1.2
  let room = if upright { calc.min(space.width, space.height * 0.5) } else { calc.min(space.height, space.width * 0.5) }
  let modules = data.at("qrModules", default: 29)
  let dots = calc.floor(room / (modules * dot))
  let side = if dots >= 1 { modules * dots * dot } else { room }
  let code = image("qr.svg", width: side, height: side)

  if upright {
    grid(rows: (side, 1fr), row-gutter: 1mm, align(center, code), pad(x: 2.5mm, bottom: 1.5mm, body))
  } else {
    grid(columns: (side, 1fr), column-gutter: 1mm, align(horizon, code), body)
  }
})

/// Sets up the page at the label's size and prints the design once per copy.
#let sheet(margin: 2mm, body) = {
  set page(width: width, height: height, margin: snap(margin))
  set text(font: serif, lang: "en", region: "GB", hyphenate: false, fill: black)
  set par(leading: 0.38em, spacing: 0.55em, justify: false)
  set block(spacing: 0.45em)

  for copy in range(data.copies) {
    if copy > 0 { pagebreak() }
    body
  }
}
