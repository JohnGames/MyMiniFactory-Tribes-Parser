using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

if (args.Length < 2)
{
    Console.WriteLine("Usage: ParserForTribesData.exe <input.csv> <output.csv>");
    return;
}

var inputPath = args[0];
var outputPath = args[1];

var records = new List<Record>();
var culture = CultureInfo.InvariantCulture;

using (var reader = new StreamReader(inputPath))
using (var csv = new CsvReader(reader, new CsvConfiguration(culture) { HeaderValidated = null, MissingFieldFound = null }))
{
    csv.Read();
    csv.ReadHeader();
    while (csv.Read())
    {
        var date = DateTime.Parse(csv.GetField("Date"));
        var username = csv.GetField("Triber Username");
        var price = decimal.Parse(csv.GetField("Tier Price"), culture);
        var tier = csv.GetField("Tier");

        records.Add(new Record
        {
            Date = date,
            Username = username,
            Price = price,
            Tier = tier
        });
    }
}

// Detect likely annual price by a simple min/max lookup
// This can be tricked if you duplicated tier names to later adjust pricing
var tierPriceMin = new Dictionary<string, decimal>();
var tierPriceMax = new Dictionary<string, decimal>();

foreach (var r in records)
{
    if (!tierPriceMin.ContainsKey(r.Tier) || r.Price < tierPriceMin[r.Tier])
        tierPriceMin[r.Tier] = r.Price;
    if (!tierPriceMax.ContainsKey(r.Tier) || r.Price > tierPriceMax[r.Tier])
        tierPriceMax[r.Tier] = r.Price;
}


var monthlyData = new SortedDictionary<string, Dictionary<string, TierStats>>();
var annualSubscriptions = new List<AnnualRecord>();

foreach (var r in records)
{
    var ym = $"{r.Date:yyyy-MM}";
    if (!monthlyData.ContainsKey(ym))
        monthlyData[ym] = new Dictionary<string, TierStats>();

    var dict = monthlyData[ym];

    if (!dict.ContainsKey(r.Tier))
        dict[r.Tier] = new TierStats();

    dict[r.Tier].Members.Add(r.Username);

    var isAnnual = r.Price > tierPriceMin[r.Tier];

    if (isAnnual)
    {
        dict[r.Tier].Annuals.Add(r.Username);
        annualSubscriptions.Add(new AnnualRecord
        {
            StartMonth = new DateTime(r.Date.Year, r.Date.Month, 1),
            Username = r.Username,
            Tier = r.Tier
        });
    }
}

// Looks ahead next 11 months and adds annuals to that tier, skipping the month they start in.
foreach (var ar in annualSubscriptions)
{
    for (int i = 1; i < 12; i++)
    {
        var m = ar.StartMonth.AddMonths(i);
        var ym = $"{m:yyyy-MM}";

        if (!monthlyData.ContainsKey(ym))
            monthlyData[ym] = new Dictionary<string, TierStats>();

        var dict = monthlyData[ym];
        if (!dict.ContainsKey(ar.Tier))
            dict[ar.Tier] = new TierStats();

        dict[ar.Tier].Members.Add(ar.Username);
        dict[ar.Tier].Annuals.Add(ar.Username);
    }
}

var allTiers = new HashSet<string>();
var allMonths = monthlyData.Keys.OrderBy(k => k).ToList();

foreach (var month in monthlyData.Values)
foreach (var tier in month.Keys)
    allTiers.Add(tier);

using var csvWriter = new StreamWriter(outputPath);
csvWriter.Write("Tier");
foreach (var ym in allMonths)
{
    var dt = DateTime.ParseExact(ym + "-01", "yyyy-MM-dd", culture);
    csvWriter.Write($",{dt:MMMM yyyy}");
}
csvWriter.WriteLine();

foreach (var tier in allTiers.OrderBy(t => t))
{
    csvWriter.Write(tier);
    foreach (var ym in allMonths)
    {
        var count = monthlyData[ym].TryGetValue(tier, out var stats) ? stats.Members.Count : 0;
        csvWriter.Write($",{count}");
    }
    csvWriter.WriteLine();
}

record Record
{
    public DateTime Date { get; set; }
    public required string Username { get; set; }
    public required decimal Price { get; set; }
    public required string Tier { get; set; }
}

record AnnualRecord
{
    public DateTime StartMonth { get; set; }
    public required string Username { get; set; }
    public required string Tier { get; set; }
}

class TierStats
{
    public HashSet<string> Members { get; } = new();
    public HashSet<string> Annuals { get; } = new();
}
