// One UK plant passport for a whole package: "UK Plant Passport" and part B at the top of every page,
// then a row for each batch giving its own botanical name (A), traceability code (C) and origin (D), so
// each code and origin stays with its plant. A long package carries on over more pages, each a whole
// passport with its border. Every part arrives in data.json and is only ever placed as text.
#import "Shared.typ": *

#let border = 2.5mm
#set page(
  width: width,
  height: height,
  margin: (x: border + 3mm, top: border + 22mm, bottom: border + 6mm),
  background: place(top + left, dx: border, dy: border, rect(width: width - 2 * border, height: height - 2 * border, stroke: rule)),
  header-ascent: 0pt,
  header: context [
    #v(border + 3mm)
    #text(weight: "bold", size: 13pt)[UK Plant Passport]
    #h(1fr)
    #if counter(page).final().first() > 1 [#text(size: 8pt)[Page #counter(page).display()#if data.copies == 1 [ of #counter(page).final().first()]]]
    #v(1mm)
    #grid(columns: (auto, 1fr), column-gutter: 2mm, text(weight: "bold", font: mono)[B], text(font: mono, item.partB))
    #grid(columns: (auto, 1fr), column-gutter: 2mm, text(weight: "bold", font: mono)[E], [])
  ],
  footer-descent: 0pt,
  footer: align(right, text(size: 7pt, item.package)),
)
#set text(font: serif, lang: "en", region: "GB", hyphenate: false, fill: black, size: 9pt)

#context {
  let room = width - 2 * border - 6mm
  if room < 45mm or height < 65mm or not words-fit(item.partB, room - 5mm, words => text(font: mono, words)) {
    panic("The label stock is too small for this passport. Choose a larger label.")
  }
  for line in item.lines {
    if not words-fit(line.partA, room * 0.5, words => text(style: "italic", words)) or not words-fit(line.partC, room * 0.25, words => text(font: mono, words)) {
      panic("The label stock is too small for this passport. Choose a larger label.")
    }
  }
}

#for copy in range(data.copies) {
  if copy > 0 { pagebreak() }
  counter(page).update(1)
  table(
    columns: (1fr, auto, auto),
    stroke: (x, y) => if y == 0 { (bottom: rule) } else { (bottom: 0.4pt + luma(170)) },
    inset: (x: 1.2mm, y: 1.4mm),
    align: (x, y) => if x == 2 { center } else { left },
    table.header(
      text(weight: "bold")[A #h(1mm) #text(weight: "regular", size: 7pt)[Botanical name]],
      text(weight: "bold")[C #h(1mm) #text(weight: "regular", size: 7pt)[Code]],
      text(weight: "bold")[D],
    ),
    ..item.lines.map(line => (
      text(style: "italic", line.partA),
      text(font: mono, line.partC),
      text(font: mono, line.partD),
    )).flatten(),
  )
}
