import type { Schemas } from "./types";

export interface DraftLine {
  description: string;
  quantity: number;
  unitPrice: number;
  vatRate: number;
  plantId: string | null;
  containerId: string | null;
  batchId: string | null;
}

export const vatRates = [20, 5, 0];

const round = (value: number) => Math.round(value * 100) / 100;

/** The same sums the invoice shows: VAT is worked out for each rate, then rounded. */
export function totals(lines: DraftLine[]): {
  net: number;
  vat: { rate: number; vat: number }[];
  total: number;
} {
  const net = round(
    lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0),
  );
  const rates = [...new Set(lines.map((line) => line.vatRate))]
    .filter((rate) => rate > 0)
    .sort((a, b) => b - a);
  const vat = rates.map((rate) => ({
    rate,
    vat: round(
      (lines
        .filter((line) => line.vatRate === rate)
        .reduce((sum, line) => sum + line.quantity * line.unitPrice, 0) *
        rate) /
        100,
    ),
  }));

  return {
    net,
    vat,
    total: round(net + vat.reduce((sum, item) => sum + item.vat, 0)),
  };
}

export type InvoiceSummary = Schemas["InvoiceSummary"];

/** Where an invoice stands, in a word or two. */
export function standing(invoice: InvoiceSummary, today: string): string {
  if (invoice.kind === "CreditNote") {
    return "Credit note";
  }

  if (invoice.credited) {
    return "Cancelled";
  }

  if (invoice.paidOn) {
    return "Paid";
  }

  return invoice.dueOn && invoice.dueOn < today ? "Overdue" : "Owed";
}

export function invoiceNumber(number: number | string): string {
  return String(number).padStart(4, "0");
}
