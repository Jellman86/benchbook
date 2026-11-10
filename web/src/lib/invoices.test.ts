import { describe, expect, it } from "vitest";
import { invoiceNumber, standing, totals, type DraftLine } from "./invoices";

const line = (quantity: number, unitPrice: number, vatRate = 0): DraftLine => ({
  description: "Lavender",
  quantity,
  unitPrice,
  vatRate,
  plantId: null,
  containerId: null,
  batchId: null,
});

describe("invoice totals", () => {
  it("adds VAT for each rate", () => {
    expect(totals([line(10, 3, 20), line(10, 2, 0)])).toEqual({
      net: 50,
      vat: [{ rate: 20, vat: 6 }],
      total: 56,
    });
  });

  it("rounds to the penny", () => {
    expect(totals([line(3, 0.333, 20)]).total).toBe(1.2);
  });
});

describe("invoice standing", () => {
  const base = {
    id: "i",
    number: 1,
    kind: "Invoice",
    customerName: "Hilltop",
    issuedOn: "2026-10-01",
    dueOn: "2026-10-31",
    paidOn: null,
    total: 10,
    credited: false,
    creditsInvoiceId: null,
  } as const;

  it("says when it's overdue", () => {
    expect(standing(base, "2026-11-10")).toBe("Overdue");
    expect(standing(base, "2026-10-15")).toBe("Owed");
    expect(standing({ ...base, paidOn: "2026-10-20" }, "2026-11-10")).toBe(
      "Paid",
    );
    expect(standing({ ...base, credited: true }, "2026-11-10")).toBe(
      "Cancelled",
    );
  });

  it("pads numbers", () => {
    expect(invoiceNumber(7)).toBe("0007");
  });
});
