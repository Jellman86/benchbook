#let data = json("data.json")
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: 9pt, font: "Libertinus Serif")
#set par(leading: 0.4em)

#grid(
  columns: (auto, 1fr),
  column-gutter: 2mm,
  image("qr.svg", height: 100%),
  block(height: 100%, clip: true)[
    #text(size: 13pt, weight: "bold", font: "DejaVu Sans Mono", data.code)
    #for line in data.lines [
      #v(0.5mm)
      #text(size: 10pt, weight: "bold", line.plant)
      #if line.botanical != "" [ \ #text(style: "italic", line.botanical) ]
      \ #text(size: 8pt, font: "DejaVu Sans Mono", line.batch)
    ]
  ],
)
