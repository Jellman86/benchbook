// An A4 page of stickers. Each label is already drawn, by its own design, at the size of one sticker; this only
// puts them where the stickers are. Faint outlines help when printing on plain paper to cut out.
#let data = json("sheet.json")
#set page(width: 210mm, height: 297mm, margin: 0mm)

#for (number, cells) in data.pages.enumerate() {
  if number > 0 { pagebreak() }
  for cell in cells {
    place(
      top + left,
      dx: cell.x * 1mm,
      dy: cell.y * 1mm,
      box(
        width: cell.w * 1mm,
        height: cell.h * 1mm,
        stroke: if data.outlines { 0.3pt + luma(170) } else { none },
        image(cell.file, width: cell.w * 1mm, height: cell.h * 1mm),
      ),
    )
  }
}

// Nothing to place still makes a blank page rather than an empty file.
#if data.pages.len() == 0 { [] }
