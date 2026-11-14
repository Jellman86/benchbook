using Hortiplanner.Api.Printing;

namespace Hortiplanner.Api.Data;

/// <summary>A label printer on the network that Hortiplanner sends labels straight to.</summary>
internal sealed class Printer : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Its IP address or network name.</summary>
    public required string Address { get; set; }

    /// <summary>Where it listens for print jobs: 9100 on almost every label printer.</summary>
    public int Port { get; set; } = 9100;

    public PrinterLanguage Language { get; set; }

    /// <summary>Its print head's dots per inch: usually 203 or 300.</summary>
    public int DotsPerInch { get; set; } = 300;

    /// <summary>The kinds of label it prints, by name, such as PlantLabel.</summary>
    public string[] Labels { get; set; } = [];
}
