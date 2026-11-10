using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Printing;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

// These draw labels with the real Typst program, so it must be installed.
public sealed class PrinterTests(PostgresFixture database)
{
    /// <summary>A pretend label printer: it listens on this computer and keeps whatever it is sent.</summary>
    private sealed class PretendPrinter : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public PretendPrinter() => listener.Start();

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public async Task<byte[]> ReceiveAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(Cancellation);
            await using var stream = client.GetStream();
            using var received = new MemoryStream();
            await stream.CopyToAsync(received, Cancellation);
            return received.ToArray();
        }

        public void Dispose() => listener.Dispose();
    }

    private static object Printer(string name, int port, string language = "Zpl", string[]? labels = null) => new
    {
        name,
        address = "127.0.0.1",
        port,
        language,
        dotsPerInch = 300,
        labels = labels ?? ["ContainerLabel", "PlaceLabel"],
    };

    private static async Task<Guid> AddPrinterAsync(HttpClient owner, object printer) =>
        Json(await PostForTextAsync(owner, "/api/printers", printer)).GetProperty("id").GetGuid();

    private static Task<TwoPersonNursery> NurseryAsync(PostgresFixture database, bool allowLocal = true) =>
        TwoPersonNursery.CreateAsync(database, settings: [("Printing:AllowLocal", allowLocal ? "true" : "false")]);

    private static async Task<string> StartBatchAsync(HttpClient client)
    {
        var plant = Json(await PostForTextAsync(client, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", xylellaHost = false, protectedVariety = false, prices = Array.Empty<object>() }))
            .GetProperty("id").GetGuid();
        var started = Json(await PostForTextAsync(client, "/api/batches", new
        {
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId = plant,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
        }));
        return started.GetProperty("labelCode").GetString()!;
    }

    [Fact]
    public async Task The_owner_sets_up_printers_and_everyone_can_see_them()
    {
        await using var nursery = await NurseryAsync(database);
        await AddPrinterAsync(nursery.Owner, Printer("Potting shed", 9100));

        var printer = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/printers"), Cancellation)).EnumerateArray());
        Assert.Equal("Potting shed", printer.GetProperty("name").GetString());
        Assert.Equal(2, printer.GetProperty("labels").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/printers", Printer("POTTING SHED", 9100)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/printers", Printer("Odd port", 70000)));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/printers", new { name = "Bad", address = "http://x/", port = 9100, language = "Zpl", dotsPerInch = 300, labels = Array.Empty<string>() }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/printers", Printer("Invoices", 9100, labels: ["Invoice"])));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/printers", Printer("Mine", 9100)));
    }

    [Fact]
    public async Task A_tray_label_goes_straight_to_a_zpl_printer_as_one_picture_with_its_copies()
    {
        using var shed = new PretendPrinter();
        await using var nursery = await NurseryAsync(database);
        var printer = await AddPrinterAsync(nursery.Owner, Printer("Potting shed", shed.Port));
        var label = await StartBatchAsync(nursery.Staff);

        var received = shed.ReceiveAsync();
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Staff, $"/api/printers/{printer}/print", new { kind = "ContainerLabel", target = label, copies = 3 }));
        var job = Encoding.ASCII.GetString(await received);

        // 50 mm at 300 dots an inch is 591 dots wide.
        Assert.StartsWith("^XA^PW591^LL", job, StringComparison.Ordinal);
        Assert.Contains("^GFA,", job, StringComparison.Ordinal);
        Assert.EndsWith("^PQ3^XZ", job, StringComparison.Ordinal);
        Assert.Contains('F', job[job.IndexOf("^GFA", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task A_test_print_to_a_tspl_printer_says_the_label_size()
    {
        using var shed = new PretendPrinter();
        await using var nursery = await NurseryAsync(database);
        var printer = await AddPrinterAsync(nursery.Owner, Printer("Shop", shed.Port, "Tspl", ["PlantLabel"]));

        var received = shed.ReceiveAsync();
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/printers/{printer}/test"));
        var job = await received;

        Assert.StartsWith("SIZE 50 mm,25 mm\r\nGAP 2 mm,0 mm", Encoding.ASCII.GetString(job), StringComparison.Ordinal);
        Assert.EndsWith("PRINT 1,1\r\n", Encoding.ASCII.GetString(job), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_printer_that_does_not_answer_is_reported()
    {
        int closedPort;

        using (var gone = new PretendPrinter())
        {
            closedPort = gone.Port;
        }

        await using var nursery = await NurseryAsync(database);
        var printer = await AddPrinterAsync(nursery.Owner, Printer("Switched off", closedPort));

        using var response = await nursery.Owner.PostAsync(Route($"/api/printers/{printer}/test"), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("unreachable", Json(await response.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
    }

    [Fact]
    public async Task The_server_never_prints_to_itself()
    {
        await using var nursery = await NurseryAsync(database, allowLocal: false);
        var printer = await AddPrinterAsync(nursery.Owner, Printer("Sneaky", 5432));

        using var response = await nursery.Owner.PostAsync(Route($"/api/printers/{printer}/test"), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("not-allowed", Json(await response.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
    }

    [Theory]
    [InlineData("192.168.1.50", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void Only_addresses_a_printer_could_have_are_used(string address, bool usable) =>
        Assert.Equal(usable, PrinterConnection.IsPrinterAddress(IPAddress.Parse(address)));
}
