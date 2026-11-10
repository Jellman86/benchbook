#let data = json("data.json")
#let item = data.item
#let style = data.style
#let size = (Compact: 12pt, Standard: 16pt, Large: 22pt).at(style.layout)
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: size * 0.6, font: "Libertinus Serif")
#set par(leading: 0.4em)

#for copy in range(data.copies) [
  #if copy > 0 { pagebreak() }
  #grid(
    columns: (auto, 1fr),
    column-gutter: 2mm,
    image("qr.svg", height: 100%),
    block(height: 100%, clip: true)[
      #text(size: size, weight: "bold", item.name)
      #if item.path != item.name [ \ #text(item.path) ]
      #v(1mm)
      #text(size: size * 0.75, weight: "bold", font: "DejaVu Sans Mono", item.code)
      #if style.showLogo [ #v(1fr) #image(data.logo, height: size * 1.4) ]
      #if style.showNurseryName [ \ #text(size: size * 0.5, data.nursery) ]
    ],
  )
]
