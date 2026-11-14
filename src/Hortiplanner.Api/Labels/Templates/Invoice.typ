#let data = json("data.json")
#let doc = data.item
#set page(
  width: float(sys.inputs.width) * 1mm,
  height: float(sys.inputs.height) * 1mm,
  margin: (x: 18mm, y: 16mm),
)
#set text(size: 10pt, font: "Libertinus Serif")
#set par(leading: 0.5em)

#grid(
  columns: (1fr, auto),
  column-gutter: 8mm,
  [
    #if data.style.showLogo [ #image(data.logo, height: 18mm) #v(2mm) ]
    #text(size: 14pt, weight: "bold", doc.seller.name)
    #for line in doc.seller.address [ \ #line ]
    #if doc.seller.contact != "" [ \ #text(size: 9pt, doc.seller.contact) ]
    #if doc.seller.vat != "" [ \ #text(size: 9pt, doc.seller.vat) ]
  ],
  align(right)[
    #text(size: 22pt, weight: "bold", doc.title) \
    No. #doc.number \
    #doc.issued
    #if doc.due != "" [ \ Due by #doc.due ]
    #if doc.credits != "" [ \ #doc.credits ]
    #if doc.paid != "" [ \ *#doc.paid* ]
  ],
)

#v(10mm)
#text(size: 9pt, fill: luma(90))[To] \
*#doc.buyer.name*
#for line in doc.buyer.address [ \ #line ]
#if doc.buyer.vat != "" [ \ #doc.buyer.vat ]

#v(8mm)
#let header = if doc.vatCharged {
  ([*Item*], [*Qty*], [*Each*], [*VAT*], [*Amount*])
} else {
  ([*Item*], [*Qty*], [*Each*], [*Amount*])
}
#let rows = doc.lines.map(line => if doc.vatCharged {
  (line.description, line.quantity, line.unit, line.rate, line.net)
} else {
  (line.description, line.quantity, line.unit, line.net)
}).flatten()

#table(
  columns: if doc.vatCharged { (1fr, auto, auto, auto, auto) } else { (1fr, auto, auto, auto) },
  align: (col, row) => if col == 0 { left } else { right },
  stroke: (x, y) => if y == 0 { (bottom: 0.6pt) } else { (bottom: 0.3pt + luma(200)) },
  inset: (x: 4pt, y: 5pt),
  ..header,
  ..rows,
)

#v(4mm)
#align(right)[
  #grid(
    columns: (auto, 28mm),
    row-gutter: 2mm,
    column-gutter: 6mm,
    align: (right, right),
    ..if doc.vatCharged {
      ([Net], doc.net) + doc.vat.map(rate => ([VAT at #rate.rate], rate.vat)).flatten()
    } else { () },
    text(weight: "bold")[Total], text(weight: "bold", doc.total),
  )
]

#if not doc.vatCharged [
  #v(4mm)
  #text(size: 9pt)[No VAT is charged: the nursery is not registered for VAT.]
]

#if doc.notes != "" [
  #v(8mm)
  #doc.notes
]
