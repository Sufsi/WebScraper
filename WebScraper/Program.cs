using HtmlAgilityPack;

string baseUrl = "https://books.toscrape.com/";
string outputDirectory = @"C:\Users\Xuna9\Documents\Scrape";

// HashSet to store visited URLs
var visitedPages = new HashSet<string>();

// HashSets to store visited resources and images
var visitedResources = new HashSet<string>();
var visitedImages = new HashSet<string>();

Directory.CreateDirectory(outputDirectory);

await ScrapeUrl(baseUrl, outputDirectory);

Console.WriteLine("Scraping completed.");

async Task ScrapeUrl(string url, string outputDirectory)
{
    if (visitedPages.Contains(url))
    {
        return; // Skip visiting if already visited
    }

    visitedPages.Add(url); // Mark the current page as visited

    var httpClient = new HttpClient();
    var html = await httpClient.GetStringAsync(url);
    var htmlDocument = new HtmlDocument();
    htmlDocument.LoadHtml(html);
}