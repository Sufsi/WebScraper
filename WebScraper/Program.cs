using HtmlAgilityPack;
using System.Collections.Concurrent;

string baseOutputDirectory;
do
{
    Console.WriteLine("Please input your desired folderpath:");
    baseOutputDirectory = Console.ReadLine();
} while (!Directory.Exists(baseOutputDirectory));


//Init param
int totalImages = 2000; //Books have a overview image and the detailimage so books*2
int totalDetailPages = 1000;

//HoldVisitedPages
var visitedPages = new ConcurrentBag<string>();
var visitedResources = new ConcurrentBag<string>();
var visitedImages = new ConcurrentBag<string>();
var visitedDetailPages = new ConcurrentBag<string>();
var detailPageUrls = new ConcurrentBag<string>();


//ProgressBar
object consoleLock = new();
double lastProgress = 0.0;


var totalPages = new ConcurrentBag<String>();
for (int i = 1; i < 51; i++)
{
    totalPages.Add($"https://books.toscrape.com/catalogue/category/books_1/page-{i}.html");
}

var outputDetailDir = Path.Combine(baseOutputDirectory, "catalogue");

Directory.CreateDirectory(baseOutputDirectory);
Directory.CreateDirectory(outputDetailDir);

Console.WriteLine("Scraping started...");
UpdateProgressBar();

while (visitedImages.Count < totalImages || visitedPages.Count < totalPages.Count || visitedDetailPages.Count < totalDetailPages)
{
    ScrapePages();
}

Console.WriteLine("\nScraping completed.");
Console.WriteLine("Press any key to exit...");
Console.ReadLine();

void ScrapePages()
{
    var httpClient = new HttpClient();
    Parallel.ForEach(
    totalPages,
    new ParallelOptions { MaxDegreeOfParallelism = 5 },
    async currentPage =>
    {
        await ScrapeUrl(currentPage, baseOutputDirectory, httpClient);
    }
);
    if (!detailPageUrls.IsEmpty)
    {
        Parallel.ForEach(
        detailPageUrls,
        new ParallelOptions { MaxDegreeOfParallelism = 5 },
        async detailPage =>
        {
            await ScrapeUrl(detailPage, outputDetailDir, httpClient);
        }
        );
    }
}

async Task ScrapeUrl(string url, string outputDirectory, HttpClient httpClient)
{
    if (visitedPages.Contains(url))
    {
        //Skip visiting if already visited
        return;
    }

    //Mark the current page as visited
    visitedPages.Add(url);

    var html = await httpClient.GetStringAsync(url);
    var htmlDocument = new HtmlDocument();
    htmlDocument.LoadHtml(html);
    if (outputDirectory.Equals(baseOutputDirectory))
    {
        //For base pages, download resources and save the HTML document
        await DownloadResources(htmlDocument, url, httpClient);
        SaveHtml(htmlDocument, url, outputDirectory);
    }
    else if (outputDirectory.Equals(outputDetailDir))
    {
        //For detail pages, save the HTML document directly
        await DownloadResources(htmlDocument, url, httpClient);
        SaveDetailPage(htmlDocument, url, outputDirectory);
    }

    UpdateProgressBar();
}

void SaveDetailPage(HtmlDocument document, string url, string outputDirectory)
{
    try
    {
        if (document != null && document.DocumentNode != null)
        {
            var fileName = url.Substring(url.IndexOf("catalogue/") + "catalogue/".Length).TrimEnd('/').Replace("/index.html", "") + ".html";
            var filePath = Path.Combine(outputDirectory, fileName);

            UpdateHtmlContent(document);

            File.WriteAllText(filePath, document.DocumentNode.OuterHtml);
            visitedDetailPages.Add(url);
        }
        else
        {
            Console.WriteLine("Error: Document or DocumentNode is null.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving HTML: {ex.Message}");
    }
}

void SaveHtml(HtmlDocument document, string url, string outputDirectory)
{
    try
    {
        if (document != null && document.DocumentNode != null)
        {
            var fileName = Path.GetFileName(url) + ".html";
            var filePath = Path.Combine(outputDirectory, fileName);

            UpdateHtmlContent(document);

            File.WriteAllText(filePath, document.DocumentNode.OuterHtml);
        }
        else
        {
            Console.WriteLine("Error: Document or DocumentNode is null.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving HTML: {ex.Message}");
    }
}

async Task DownloadResources(HtmlDocument document, string url, HttpClient httpClient)
{
    await DownloadImages(document, url, httpClient);
    await DownloadCssAndJs(document, url, httpClient);
}

async Task DownloadImages(HtmlDocument document, string url, HttpClient httpClient)
{
    var imageNodes = document.DocumentNode.SelectNodes("//img[@src]");
    if (imageNodes != null)
    {
        foreach (var imageNode in imageNodes)
        {
            var imageUrl = imageNode.Attributes["src"].Value;
            var absoluteUrl = new Uri(new Uri(url), imageUrl).AbsoluteUri;

            if (!visitedImages.Contains(absoluteUrl))
            {
                //Mark image URL as visited
                visitedImages.Add(absoluteUrl);

                var fileName = Path.GetFileName(absoluteUrl);
                var filePath = Path.Combine(baseOutputDirectory, "images", fileName);

                var imageBytes = await httpClient.GetByteArrayAsync(absoluteUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytesAsync(filePath, imageBytes);
            }
        }
    }
    ExtractDetaiPageUrls(document, url);
}

async Task DownloadCssAndJs(HtmlDocument document, string url, HttpClient httpClient)
{
    var resourceNodes = document.DocumentNode.SelectNodes("//link[@href] | //script[@src]");
    if (resourceNodes != null)
    {
        foreach (var resourceNode in resourceNodes)
        {
            string resourceUrl;
            if (resourceNode.Name == "link")
            {
                resourceUrl = resourceNode.Attributes["href"].Value;
            }
            else
            {
                resourceUrl = resourceNode.Attributes["src"].Value;
            }

            var absoluteUrl = new Uri(new Uri(url), resourceUrl).AbsoluteUri;

            if (!visitedResources.Contains(absoluteUrl))
            {
                //Mark resource URL as visited
                visitedResources.Add(absoluteUrl);

                var fileName = Path.GetFileName(absoluteUrl);
                var filePath = Path.Combine(baseOutputDirectory, "resources", fileName);


                var resourceBytes = await httpClient.GetByteArrayAsync(absoluteUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytesAsync(filePath, resourceBytes);
            }
        }
    }
}

void ExtractDetaiPageUrls(HtmlDocument document, string url)
{
    var detaiNodes = document.DocumentNode.SelectNodes("//h3/a[@href]");
    if (detaiNodes != null)
    {
        foreach (var detailNode in detaiNodes)
        {
            var href = detailNode.Attributes["href"].Value;
            var absoluteUrl = new Uri(new Uri(url), href).AbsoluteUri;

            if (Uri.IsWellFormedUriString(absoluteUrl, UriKind.Absolute))
            {
                detailPageUrls.Add(absoluteUrl);
            }
        }
    }
}

void UpdateHtmlContent(HtmlDocument htmlDocument)
{
    //Update image URLs
    var imageNodes = htmlDocument.DocumentNode.SelectNodes("//img[@src]");
    if (imageNodes != null)
    {
        foreach (var imageNode in imageNodes)
        {
            var imageUrl = imageNode.Attributes["src"].Value;
            var localImagePath = Path.Combine(baseOutputDirectory, "images", Path.GetFileName(imageUrl));

            //Update the src attribute to point to the local copy
            imageNode.SetAttributeValue("src", localImagePath);
        }
    }

    //Update CSS URLs
    var linkNodes = htmlDocument.DocumentNode.SelectNodes("//link[@href]");
    if (linkNodes != null)
    {
        foreach (var linkNode in linkNodes)
        {
            var cssUrl = linkNode.Attributes["href"].Value;
            var localCssPath = Path.Combine(baseOutputDirectory, "resources", Path.GetFileName(cssUrl));

            //Update the href attribute to point to the local copy
            linkNode.SetAttributeValue("href", localCssPath);
        }
    }
}
void UpdateProgressBar()
{
    double imagesProgress = Math.Min((double)visitedImages.Count / totalImages, 1.0);
    double pagesProgress = Math.Min((double)visitedPages.Count / totalPages.Count, 1.0);
    double detailPagesProgress = Math.Min((double)visitedDetailPages.Count / totalDetailPages, 1.0);

    double overallProgress = (imagesProgress + pagesProgress + detailPagesProgress) / 3;

    lock (consoleLock)
    {
        if (Math.Abs(overallProgress - lastProgress) >= 0.01)
        {
            lastProgress = overallProgress;
            Console.Clear();
            Console.Write("\r[");
            int progressWidth = 50;
            int progressBarPosition = (int)(overallProgress * progressWidth);
            for (int i = 0; i < progressWidth; i++)
            {
                if (i < progressBarPosition)
                    Console.Write("=");
                else
                    Console.Write(" ");
            }
            Console.Write($"] {overallProgress:P0}");
            Console.Out.Flush();
        }
    }
}