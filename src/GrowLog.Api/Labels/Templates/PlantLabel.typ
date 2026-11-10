#let data = json("data.json")
#let item = data.item
#let style = data.style
#let size = (Compact: 10pt, Standard: 13pt, Large: 17pt).at(style.layout)
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: size * 0.8, font: "Libertinus Serif")
#set par(leading: 0.35em)

#let words = block(height: 100%, clip: true)[
  #text(size: size, weight: "bold", item.plant)
  #if style.showBotanical and item.botanical != "" [ \ #text(style: "italic", item.botanical) ]
  #if style.showCare and item.care != "" [ \ #text(size: size * 0.65, item.care) ]
  #if style.showPrice and item.price != "" [ \ #text(size: size * 1.05, weight: "bold", item.price) ]
  #if style.showNurseryName [ \ #text(size: size * 0.6, data.nursery) ]
]

#for copy in range(data.copies) [
  #if copy > 0 { pagebreak() }
  #if style.showLogo {
    grid(columns: (auto, 1fr), column-gutter: 2mm, image(data.logo, height: 100%), words)
  } else {
    words
  }
]
