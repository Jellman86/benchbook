#let data = json("data.json")
#let item = data.item
#let style = data.style
#let size = (Compact: 20pt, Standard: 26pt, Large: 34pt).at(style.layout)
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 8mm,
)
#set text(size: size * 0.54, font: "Libertinus Serif")

#for copy in range(data.copies) [
  #if copy > 0 { pagebreak() }
  #block(height: 100%, clip: true)[
    #if style.showLogo or style.showNurseryName [
      #grid(
        columns: (auto, 1fr),
        column-gutter: 3mm,
        align: horizon,
        if style.showLogo { image(data.logo, height: size * 1.1) } else { [] },
        if style.showNurseryName { text(size: size * 0.5, weight: "bold", data.nursery) } else { [] },
      )
      #v(4mm)
    ]
    #text(size: size, weight: "bold", item.plant)
    #if style.showBotanical and item.botanical != "" [ \ #text(size: size * 0.62, style: "italic", item.botanical) ]

    #if style.showCare [
      #v(6mm)
      #if item.position != "" [ *Position:* #item.position \ ]
      #if item.watering != "" [ *Watering:* #item.watering \ ]
      #if item.hardiness != "" [ *Hardiness:* #item.hardiness ]
    ]

    #if style.showPrice [
      #v(6mm)
      #for entry in item.prices [
        #text(size: size * 0.7)[#entry.size: *#entry.price*] \
      ]
    ]
  ]
]
