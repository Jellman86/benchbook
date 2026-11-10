#let data = json("data.json")
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: 10pt, font: "Libertinus Serif")
#set par(leading: 0.4em)

#grid(
  columns: (auto, 1fr),
  column-gutter: 2mm,
  image("qr.svg", height: 100%),
  block(height: 100%, clip: true)[
    #text(size: 16pt, weight: "bold", data.name)
    #if data.path != data.name [ \ #text(size: 9pt, data.path) ]
    #v(1mm)
    #text(size: 12pt, weight: "bold", font: "DejaVu Sans Mono", data.code)
  ],
)
