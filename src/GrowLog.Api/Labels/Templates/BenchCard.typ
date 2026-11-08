#let data = json("data.json")
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 8mm,
)
#set text(size: 14pt, font: "Libertinus Serif")

#block(height: 100%, clip: true)[
  #text(size: 26pt, weight: "bold", data.plant)
  #if data.botanical != "" [ \ #text(size: 16pt, style: "italic", data.botanical) ]

  #v(6mm)
  #if data.position != "" [ *Position:* #data.position \ ]
  #if data.watering != "" [ *Watering:* #data.watering ]

  #v(6mm)
  #for entry in data.prices [
    #text(size: 18pt)[#entry.size: *#entry.price*] \
  ]
]
