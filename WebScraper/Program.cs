using HtmlAgilityPack;
using System.Collections.Concurrent;

string baseOutputDirectory;
do
{
    Console.WriteLine("Please input your desired folderpath:");
    baseOutputDirectory = Console.ReadLine();
} while (!Directory.Exists(baseOutputDirectory));

//HoldVisitedPages
var visitedPages = new ConcurrentBag<string>();

var detailPageUrls = new ConcurrentBag<string>();
var categoryUrls = new ConcurrentBag<string>();

//ProgressBar
int totalTasks = 0;
int completedTasks = 0;
object progressLock = new();


var baseProductPages = new ConcurrentBag<String>();
var baseBookPages = new ConcurrentBag<String>();

for (int i = 1; i < 51; i++)
{
    baseProductPages.Add($"https://books.toscrape.com/catalogue/page-{i}.html");
}


for (int i = 1; i < 51; i++)
{
    baseBookPages.Add($"https://books.toscrape.com/catalogue/category/books_1/page-{i}.html");
}

Console.WriteLine("Scraping started...");

await StartScraping();

Console.WriteLine("Press enter to exit");
Console.ReadLine();

async Task StartScraping()
{
    var httpClient = new HttpClient();
    var tasksQueue = new ConcurrentQueue<Task>();

    try
    {
        EnqueueTask(ScrapeUrl("https://books.toscrape.com/index.html", httpClient), tasksQueue);
        PopulateInitialTasks(httpClient, tasksQueue);

        bool onGoing = true;
        var progressBar = Task.Run(async () =>
        {
            while (completedTasks < totalTasks || !tasksQueue.IsEmpty || onGoing)
            {
                UpdateProgressBar(totalTasks, completedTasks);
                await Task.Delay(1000);
            }
            UpdateProgressBar(totalTasks, completedTasks);
        });

        var manageTasks = Task.Run(async () =>
        {
            while (!tasksQueue.IsEmpty)
            {
                if (tasksQueue.TryDequeue(out var task))
                {
                    await task;
                    ProcessNewUrls(httpClient, tasksQueue);
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        });

        await manageTasks;
        onGoing = false;
        await progressBar;

    }
    catch (Exception ex)
    {
        await Console.Out.WriteLineAsync(ex.Message);
    }
}

void EnqueueTask(Task task, ConcurrentQueue<Task> queue)
{
    lock (progressLock)
    {
        totalTasks++;
    }
    queue.Enqueue(task.ContinueWith(t =>
    {
        lock (progressLock)
        {
            completedTasks++;
        }
    }));
}

void ProcessNewUrls(HttpClient httpClient, ConcurrentQueue<Task> tasksQueue)
{
    foreach (var page in detailPageUrls)
    {
        EnqueueTask(ScrapeUrl(page, httpClient), tasksQueue);
        detailPageUrls.TryTake(out _);
    }

    foreach (var page in categoryUrls)
    {
        EnqueueTask(ScrapeUrl(page, httpClient), tasksQueue);
        categoryUrls.TryTake(out _);
    }
}

void PopulateInitialTasks(HttpClient httpClient, ConcurrentQueue<Task> queue)
{
    foreach (var url in baseProductPages)
    {
        queue.Enqueue(ScrapeUrl(url, httpClient));
    }
    foreach (var url in baseBookPages)
    {
        queue.Enqueue(ScrapeUrl(url, httpClient));
    }
}

async Task ScrapeUrl(string url, HttpClient httpClient)
{
    if (visitedPages.Contains(url))
    {
        //Skip visiting if already visited
        return;
    }
    visitedPages.Add(url);

    var html = await httpClient.GetStringAsync(url);
    var htmlDocument = new HtmlDocument();
    htmlDocument.LoadHtml(html);
    await DownloadResources(htmlDocument, url, httpClient);
    SaveHtml(htmlDocument, url);
    ExtractCategoryUrls(htmlDocument, url);
    ExtractDetaiPageUrls(htmlDocument, url);
}

void SaveHtml(HtmlDocument document, string url)
{
    try
    {
        if (document != null && document.DocumentNode != null)
        {
            var fileName = Path.GetFileName(url);
            var basePath = url.Replace("https://books.toscrape.com", baseOutputDirectory);
            var dirName = Path.GetDirectoryName(basePath);

            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }

            var filePath = Path.Combine(dirName, fileName);

            UpdateHtmlContent(document, dirName);

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
    await ImageDownloader.DownloadImages(document, url, httpClient, baseOutputDirectory);
    await ResourceDownloader.DownloadCssAndJs(document, url, httpClient, baseOutputDirectory);
}

void ExtractCategoryUrls(HtmlDocument document, string url)
{
    var categoryNodes = document.DocumentNode.SelectNodes("//ul/li/a[@href]");
    if (categoryNodes != null)
    {
        foreach (var categoryNode in categoryNodes)
        {
            var href = categoryNode.Attributes["href"].Value;
            var absoluteUrl = new Uri(new Uri(url), href).AbsoluteUri;

            if (Uri.IsWellFormedUriString(absoluteUrl, UriKind.Absolute))
            {
                categoryUrls.Add(absoluteUrl);
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

void UpdateHtmlContent(HtmlDocument htmlDocument, string dir)
{
    var imageNodes = htmlDocument.DocumentNode.SelectNodes("//img[@src]");
    if (imageNodes != null)
    {
        foreach (var imageNode in imageNodes)
        {
            var imageUrl = imageNode.Attributes["src"].Value;
            var localImagePath = Path.Combine(baseOutputDirectory, "images", Path.GetFileName(imageUrl));

            imageNode.SetAttributeValue("src", localImagePath);
        }
    }

    var hrefNodes = htmlDocument.DocumentNode.SelectNodes("//link[@href]");
    if (hrefNodes != null)
    {
        foreach (var hrefNode in hrefNodes)
        {
            var url = hrefNode.Attributes["href"].Value;
            var localPath = Path.Combine(dir, Path.GetFileName(url));

            hrefNode.SetAttributeValue("href", localPath);
        }
    }

    var headNode = htmlDocument.DocumentNode.SelectSingleNode("//head");
    if (headNode != null)
    {
        var resourceNodes = htmlDocument.DocumentNode.SelectNodes("//link[@href] | //script[@src]");
        if (resourceNodes != null)
        {
            foreach (var resourceNode in resourceNodes)
            {
                string resourceUrl;
                string localCssPath;
                if (resourceNode.Name == "link")
                {
                    resourceUrl = resourceNode.Attributes["href"].Value;
                    localCssPath = Path.Combine(baseOutputDirectory, "resources", Path.GetFileName(resourceUrl));
                    resourceNode.SetAttributeValue("href", localCssPath);
                }
                else
                {
                    resourceUrl = resourceNode.Attributes["src"].Value;
                    localCssPath = Path.Combine(baseOutputDirectory, "resources", Path.GetFileName(resourceUrl));
                    resourceNode.SetAttributeValue("src", localCssPath);
                }

            }
        }
    }

}

void UpdateProgressBar(int total, int completed)
{
    double progressPercentage = (double)completed / total;
    Console.CursorLeft = 0;
    Console.Write("Progress: [");
    int progressWidth = 50;
    int progressBarPosition = (int)(progressPercentage * progressWidth);
    for (int i = 0; i < progressWidth; i++)
    {
        Console.Write(i < progressBarPosition ? "=" : " ");
    }
    Console.Write($"] {progressPercentage:P0}");
    if (completed >= total)
    {
        Console.WriteLine("\nScraping completed.");
    }
}

public static class ImageDownloader
{
    private static HashSet<string> visitedImages = new HashSet<string>();
    private static readonly object visitedImagesLock = new object();

    public static async Task DownloadImages(HtmlDocument document, string url, HttpClient httpClient, string baseOutputDirectory)
    {
        try
        {
            var imageNodes = document.DocumentNode.SelectNodes("//img[@src]");
            if (imageNodes != null)
            {
                foreach (var imageNode in imageNodes)
                {
                    var imageUrl = imageNode.Attributes["src"].Value;
                    var absoluteUrl = new Uri(new Uri(url), imageUrl).AbsoluteUri;

                    lock (visitedImagesLock)
                    {
                        if (!visitedImages.Contains(absoluteUrl))
                        {
                            visitedImages.Add(absoluteUrl);
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var fileName = Path.GetFileName(absoluteUrl);
                    var filePath = Path.Combine(baseOutputDirectory, "images", fileName);

                    HttpResponseMessage response;
                    using (response = await httpClient.GetAsync(absoluteUrl))
                    {
                        if (!response.IsSuccessStatusCode)
                            continue;

                        using (var content = await response.Content.ReadAsStreamAsync())
                        {
                            //Ensure file operations are thread-safe
                            lock (visitedImagesLock)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                            }

                            //Write to file outside of the lock
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await content.CopyToAsync(fileStream);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}

public static class ResourceDownloader
{
    private static HashSet<string> visitedResource = new HashSet<string>();
    private static readonly object visitedResourceLock = new object();

    public static async Task DownloadCssAndJs(HtmlDocument document, string url, HttpClient httpClient, string baseOutputDirectory)
    {
        try
        {
            var headNode = document.DocumentNode.SelectSingleNode("//head");
            if (headNode != null)
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

                        lock (visitedResourceLock)
                        {
                            if (!visitedResource.Contains(absoluteUrl))
                            {
                                visitedResource.Add(absoluteUrl);
                            }
                            else
                            {
                                continue;
                            }
                        }
                        var fileName = Path.GetFileName(absoluteUrl);
                        var filePath = Path.Combine(baseOutputDirectory, "resources", fileName);

                        HttpResponseMessage response;
                        using (response = await httpClient.GetAsync(absoluteUrl))
                        {
                            if (!response.IsSuccessStatusCode)
                                continue;

                            using (var content = await response.Content.ReadAsStreamAsync())
                            {
                                //Ensure file operations are thread-safe
                                lock (visitedResourceLock)
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                                }

                                //Write to file outside of the lock
                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    await content.CopyToAsync(fileStream);
                                }
                            }
                        }
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}