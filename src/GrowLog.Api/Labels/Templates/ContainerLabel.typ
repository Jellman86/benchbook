#let data = json("data.json")
#let item = data.item
#let style = data.style
#let size = (Compact: 11pt, Standard: 13pt, Large: 16pt).at(style.layout)
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: size * 0.7, font: "Libertinus Serif")
#set par(leading: 0.4em)

#for copy in range(data.copies) [
  #if copy > 0 { pagebreak() }
  #grid(
    columns: (auto, 1fr),
    column-gutter: 2mm,
    image("qr.svg", height: 100%),
    block(height: 100%, clip: true)[
      #text(size: size, weight: "bold", font: "DejaVu Sans Mono", item.code)
      #if style.showLogo [ #h(1fr) #box(image(data.logo, height: size * 1.2)) ]
      #for line in item.lines [
        #v(0.5mm)
        #text(size: size * 0.78, weight: "bold", line.plant)
        #if style.showBotanical and line.botanical != "" [ \ #text(style: "italic", line.botanical) ]
        \ #text(size: size * 0.6, font: "DejaVu Sans Mono", line.batch)
      ]
      #if style.showNurseryName [ #v(1fr) #text(size: size * 0.55, data.nursery) ]
    ],
  )
]
