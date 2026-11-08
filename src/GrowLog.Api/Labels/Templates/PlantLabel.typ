#let data = json("data.json")
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: 2mm,
)
#set text(size: 11pt, font: "Libertinus Serif")
#set par(leading: 0.4em)

#block(height: 100%, clip: true)[
  #text(size: 13pt, weight: "bold", data.plant)
  #if data.botanical != "" [ \ #text(style: "italic", data.botanical) ]
  #if data.price != "" [ \ #text(size: 14pt, weight: "bold", data.price) ]
]
