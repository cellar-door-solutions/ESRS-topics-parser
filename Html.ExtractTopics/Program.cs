using HtmlAgilityPack;
using System.Text.Json;

var euLexUrl = "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX%3A02023R2772-20231222";
var web = new HtmlWeb();
var htmlDoc = web.Load(euLexUrl);
var languageList = htmlDoc.DocumentNode.SelectSingleNode("//ul[@class='dropdown-menu PubFormatVIEW']");
var languageLinks = languageList.SelectNodes(".//li/a").ToList();

var languages = languageLinks
    .Select(a => new
    {
        LanguageName = a.GetAttributeValue("title", "unknown"),
        ShortCode = a.InnerText.Trim()
    })
    .ToList();

foreach (var language in languages)
{
    var url = $"https://eur-lex.europa.eu/legal-content/{language.ShortCode}/TXT/HTML/?uri=CELEX:02023R2772-20231222";
    
    var projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../"));

    if (string.IsNullOrEmpty(projectRoot))
    {
        throw new InvalidOperationException("Failed to determine the project root directory.");
    }
    var outputFolder = Path.Combine(projectRoot, "Topics");
    if (!Directory.Exists(outputFolder))
    {
        Directory.CreateDirectory(outputFolder);
    }
    var outputJsonPath = Path.Combine(outputFolder, $"{language.ShortCode}.json");

    web = new HtmlWeb();
    htmlDoc = web.Load(url);

    var tables = htmlDoc.DocumentNode.SelectNodes("//table");
    var table = tables[4];
    if (table == null)
    {
        Console.WriteLine("No matching table found in the document.");
        return;
    }

    var rows = table.SelectNodes(".//tr");
    if (rows == null)
    {
        Console.WriteLine("No rows found in the table.");
        return;
    }

    var topics = new List<Topic>();
    string currentEsrs = null;
    string currentTopic = null;

    foreach (var row in rows)
    {
        if (rows.IndexOf(row) <= 1)
            continue;
        
        var cells = row.SelectNodes("./td");
        if (cells == null) continue;

        var rowValues = new string[4];
        if (cells.Count == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                rowValues[i] = i < cells.Count ? FlattenCellContent(cells[i]) : "";
            }
        } else if (cells.Count == 2)
        {
            rowValues[2] = FlattenCellContent(cells[0]);
            rowValues[3] = FlattenCellContent(cells[1]);
        }

        if (!string.IsNullOrWhiteSpace(rowValues[0]))
        {
            currentEsrs = rowValues[0];
        }
        else
        {
            rowValues[0] = currentEsrs!;
        }

        if (!string.IsNullOrWhiteSpace(rowValues[1]))
        {
            currentTopic = rowValues[1];
        }
        else
        {
            rowValues[1] = currentTopic!;
        }

        if (topics.Any(t => t.Esrs == rowValues[0]))
        {
            var esrs = topics.First(t => t.Esrs == rowValues[0]);
            esrs.SubTopics.AddRange(ParseSubItems(rowValues[2]));
            esrs.SubSubTopics.AddRange(ParseSubItems(rowValues[3]));
        }
        else
        {
            topics.Add(new Topic
            {
                Esrs = rowValues[0],
                TopicName = rowValues[1],
                SubTopics = ParseSubItems(rowValues[2]),
                SubSubTopics = ParseSubItems(rowValues[3])
            });
        }
    }

    var topicalEsrs = new TopicalEsrs
    {
        LanguageCode = language.ShortCode,
        Language = language.LanguageName,
        Topic = FlattenCellContent(rows[1].SelectNodes("./td")[1]),
        SubTopic = FlattenCellContent(rows[1].SelectNodes("./td")[2]),
        SubSubTopic = FlattenCellContent(rows[1].SelectNodes("./td")[3]),
        Topics = topics
    };
    
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    File.WriteAllText(outputJsonPath, JsonSerializer.Serialize(topicalEsrs, jsonOptions));

    Console.WriteLine($"Structured JSON data has been saved to: {outputJsonPath}"); 
}

static string FlattenCellContent(HtmlNode cell)
{
    var texts = cell.Descendants()
        .Where(node => node.NodeType == HtmlNodeType.Text)
        .Select(node => node.InnerText.Trim())
        .Where(text => !string.IsNullOrWhiteSpace(text));

    var normalizedText = System.Text.RegularExpressions.Regex.Replace(
        string.Join(" ", texts),
        @"\s+",
        " "
    ).Trim();

    return normalizedText;
}

static List<SubTopic> ParseSubItems(string content)
{
    return content
        .Split('—', StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item) && !item.Contains(":"))
        .Select(item => new SubTopic {Name = item})
        .ToList();
}

public class TopicalEsrs
{
    public required string  LanguageCode { get; set; }
    public required string Language { get; set; }
    public required string Topic { get; set; }
    public required string SubTopic { get; set; }
    public required string SubSubTopic { get; set; }
    public List<Topic> Topics { get; set; }
}

public class Topic
{
    public required string Esrs { get; set; }
    public required string TopicName { get; set; }
    public required List<SubTopic> SubTopics { get; set; }
    public required List<SubTopic> SubSubTopics { get; set; }
}

public class SubTopic
{
    public string Name { get; set; }
}
