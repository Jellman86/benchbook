namespace Hortiplanner.Api.Transfer;

internal static class ImportFields
{
    private const int Long = 2000;

    public static readonly IReadOnlyList<ImportKindFields> All =
    [
        new(ImportKind.Plants,
        [
            new("commonName", "Common name", FieldType.Text, true, ["name", "plant", "plant name"]),
            new("variety", "Variety", FieldType.Text, false, ["cultivar", "cv"]),
            new("botanicalName", "Botanical name", FieldType.Text, false, ["latin name", "scientific name", "species", "latin"]),
            new("category", "Category", FieldType.Text, false, ["type", "group"]),
            new("position", "Position", FieldType.Text, false, ["sun", "aspect", "light"]),
            new("watering", "Watering", FieldType.Text, false, ["water"]),
            new("hardiness", "Hardiness", FieldType.Text, false, ["hardy", "rhs hardiness"]),
            new("xylellaHost", "Xylella host", FieldType.YesNo, false, ["xylella"]),
            new("protectedVariety", "Protected variety", FieldType.YesNo, false, ["pbr", "plant breeders rights", "protected"]),
        ]),
        new(ImportKind.Suppliers,
        [
            new("name", "Name", FieldType.Text, true, ["supplier", "company", "supplier name"]),
            new("contactName", "Contact", FieldType.Text, false, ["contact name", "contact person"]),
            new("phone", "Phone", FieldType.Text, false, ["telephone", "tel", "mobile"]),
            new("email", "Email", FieldType.Text, false, ["e-mail", "email address"]),
            new("address", "Address", FieldType.Text, false, ["postal address"], Long),
            new("accountReference", "Account reference", FieldType.Text, false, ["account", "account number", "account no"]),
            new("supplies", "Supplies", FieldType.Text, false, ["products", "what they supply"]),
            new("typicalLeadTimeDays", "Lead time (days)", FieldType.Number, false, ["lead time", "lead time days"]),
            new("passportRegistrationNumber", "Passport registration", FieldType.Text, false, ["registration", "registration number", "plant passport number", "reg no"]),
            new("otherRegistrations", "Other registrations", FieldType.Text, false, ["other registration numbers"], Long),
            new("notes", "Notes", FieldType.Text, false, ["comments"], Long),
        ]),
        new(ImportKind.Customers,
        [
            new("name", "Name", FieldType.Text, true, ["customer", "company", "customer name", "business"]),
            new("address", "Address", FieldType.Text, false, ["postal address", "invoice address"], Long),
            new("email", "Email", FieldType.Text, false, ["e-mail", "email address"]),
            new("phone", "Phone", FieldType.Text, false, ["telephone", "tel", "mobile"]),
            new("vatNumber", "VAT number", FieldType.Text, false, ["vat", "vat no", "vat reg"]),
        ]),
    ];

    public static ImportField Find(ImportKind kind, string key) =>
        All.First(item => item.Kind == kind).Fields.First(field => string.Equals(field.Key, key, StringComparison.Ordinal));
}
