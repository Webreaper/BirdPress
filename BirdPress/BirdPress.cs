using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using HtmlAgilityPack;
using WordPressPCL;
using WordPressPCL.Models;
using WordPressPCL.Utility;

namespace BirdPress;

public class BirdPress
{
    private WordPressClient? wpClient;
    private readonly Dictionary<string, MediaItem> urlFileLookup = new();
    private BirdPressSettings? settings;

    private static void Log(string msg, params object[] args)
    {
        var line = string.Format(DateTime.Now.ToString("[yyyy-MMM-dd HH:mm:ss] ") + msg, args);
        Console.WriteLine(line);
    }

    public async Task Process()
    {
        try
        {
            var settingsJson = await File.ReadAllTextAsync("BirdPressSettings.json");
            settings = JsonSerializer.Deserialize<BirdPressSettings>(settingsJson);

            if (settings == null)
            {
                Log("No settings found (or settings format was invalid)");
                return;
            }

            var list = await GetBirds();
            
            if (!settings.wordpressBaseUrl.Contains("wp-json"))
                settings = settings with {wordpressBaseUrl = settings.wordpressBaseUrl + "/wp-json/" };
            
            Log("Initialising BirdPress...");
            Log($"  BirdNet server: {settings.birdNetUrl}");
            Log($"  WordPress Instance: {settings.wordpressBaseUrl}");
            Log($"  Post ID: {settings.wordpressPostId}");

            var wpBaseAddress = new Uri(settings.wordpressBaseUrl);
            wpClient = new WordPressClient(wpBaseAddress);
            wpClient.Auth.UseBasicAuth(settings.wordpressUser, settings.wordpressPassword);

            await PostToWordpress(list, settings.wordpressPostId);
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
    }

    private async Task UploadThumbs(IEnumerable<Species> species)
    {
        Log("Finding thumbnails for birds...");
        
        ArgumentNullException.ThrowIfNull(wpClient);
        
        foreach (var bird in species.OrderBy(x => x.common_name))
        {
            if (string.IsNullOrEmpty(bird.thumbnail_url))
            {
                Log($"No thumbnail available for {bird.common_name}");
                continue;
            }
            
            var fileName = (Path.GetFileName(bird.thumbnail_url) + ".jpg").Replace("%20", "_");

            var query = new MediaQueryBuilder();
            query.Search = fileName;
            query.Page = 0;
            query.PerPage = 10;
 
            var results = await wpClient.Media.QueryAsync(query);

            MediaItem? mediaItem = null;
            
            if (results.Any())
            {
                mediaItem = results.First(x => x.Status == MediaQueryStatus.Inherit);
            }

            if( mediaItem == null)
            {
                Log($"Uploading thumbnail for {bird.common_name}");
                
                await using var imageStream = await settings.birdNetUrl
                    .AppendPathSegment(bird.thumbnail_url)
                    .GetStreamAsync();

                mediaItem = await wpClient.Media.CreateAsync(imageStream, fileName, "image/jpeg");
            }
            else
                Log($"Found existing image for {bird.common_name}");

            urlFileLookup[bird.thumbnail_url] = mediaItem;
        }
        
        Log($"Resolved {urlFileLookup.Count()} thumbnails of {species.Count()} birds");
    }
    
    private async Task PostToWordpress(IEnumerable<Species> species, int postId)
    {
        ArgumentNullException.ThrowIfNull(wpClient);
        
        var post = await wpClient.Posts.GetByIdAsync(postId);

        await UploadThumbs(species);

        Log($"Generating post content...");

        var updatedPost = await UpdatePost(post, species);

        Log($"Updating Wordpress post for \"{updatedPost.Title?.Rendered}\"...");

        if( ! Debugger.IsAttached)
            await wpClient.Posts.UpdateAsync(updatedPost);
        else
            Log(updatedPost.Content.Rendered);
        
        Log($"Post updated successfully with {species.Count()} birds");
    }
    
    public class DateTimeConverterUsingDateTimeParse : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Assert(typeToConvert == typeof(DateTime));
            var date = DateTime.Parse(reader.GetString() ?? string.Empty);
            return date;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    
    private async Task<IEnumerable<Species>> GetBirds()
    {
        Log($"Retreiving bird list from BirdNet-Go...");

        JsonSerializerOptions options = new JsonSerializerOptions();
        options.Converters.Add(new DateTimeConverterUsingDateTimeParse());
        
        var speciesList = await settings.birdNetUrl
            .AppendPathSegment("api")
            .AppendPathSegment("v2")
            .AppendPathSegment("analytics")
            .AppendPathSegment("species")
            .AppendPathSegment("summary")
            //.AppendQueryParam("start_date", todayStr)
            //.AppendQueryParam("end_date", todayStr)
            .WithSettings(x => x.JsonSerializer = 
                        new DefaultJsonSerializer(options))
            .GetJsonAsync<IEnumerable<Species>>();

        var result =  speciesList
            .Where(x => x.max_confidence > settings.minThreshold)
            .OrderByDescending(x => x.last_heard)
            .ToList();
        
        Log($"Retreved {result.Count()} birds from BirdNet-Go");

        return result;
    }
    
    private async Task<Post> UpdatePost(Post post, IEnumerable<Species> species)
    {
        Log("Generating HTML for post update...");
        
        const string rootNodeId = "birdnet";
        var postHtml = post.Content?.Rendered;
        
        if (string.IsNullOrEmpty(postHtml))
            throw new ArgumentException("Please set up a blank post with a DIV placeholder");
        
        var doc = new HtmlDocument();
        
        doc.LoadHtml(postHtml);

        var rootNode = doc.DocumentNode
            .SelectNodes("//*[contains(., '[BirdPress]')]")?
            .FirstOrDefault();

        if (rootNode != null)
        {
            var newRootNode = doc.CreateElement("div");
            newRootNode.Id = rootNodeId;
            
            rootNode.ParentNode.InsertBefore(newRootNode, rootNode);
            rootNode.Remove();
            rootNode = newRootNode;
        }

        if (rootNode == null)
            rootNode = doc.GetElementbyId(rootNodeId);
        
        rootNode.RemoveAllChildren();

        var todayBirdsHeading = doc.CreateElement("h3");
        todayBirdsHeading.InnerHtml = $"Birds Heard Today (Last Updated: {DateTime.Now.ToString("dd-MMM-yyyy HH:mm")}):";
        rootNode.AppendChild(todayBirdsHeading);

        var tableNode = await GenerateBirdTable(rootNode, species.Where(x => x.last_heard.Date == DateTime.Now.Date));
        rootNode.AppendChild(tableNode);
        
        var allBirdsHeading = doc.CreateElement("h3");
        allBirdsHeading.InnerHtml = "Birds Heard Before Today:";
        rootNode.AppendChild(allBirdsHeading);

        var allTable = await GenerateBirdTable(rootNode, species.Where(x => x.last_heard.Date != DateTime.Now.Date));
        rootNode.AppendChild(allTable);

        var content = new Content(doc.DocumentNode.OuterHtml);
        post.Content = content;

        return post;
    }

    private async Task<HtmlNode> GenerateBirdTable(HtmlNode parentNode, IEnumerable<Species> species)
    {
        var doc = parentNode.OwnerDocument;
        var tableNode = doc.CreateElement("table");

        var header =  doc.CreateElement("tr");
        tableNode.AppendChild(header);

        AddTableCell(header, "Image", "th");
        AddTableCell(header, "Name", "th");
        AddTableCell(header, "Last Heard", "th");
        AddTableCell(header, "Accuracy", "th");

        foreach (var bird in species)
        {
            var row =  doc.CreateElement("tr");
            tableNode.AppendChild(row);

            string imageLink = "No image available";

            if (! string.IsNullOrEmpty(bird.thumbnail_url) && urlFileLookup.TryGetValue(bird.thumbnail_url, out var mediaItem))
            {
                imageLink = $"<img src=\"{mediaItem.SourceUrl}\" width=150 height=auto/>";
            }

            string format = bird.last_heard.Date == DateTime.Now.Date ? "HH:mm" : "dd-MMM-yyyy";
            
            AddTableCell(row, imageLink);
            AddTableCell(row, $"{bird.common_name} (<span style=\"font-style: italic;\">{bird.scientific_name}</span>)");
            AddTableCell(row, bird.last_heard.ToString(format));
            AddTableCell(row, bird.avg_confidence.ToString("P0"));
        }

        return tableNode;
    }
    
    private void AddTableCell(HtmlNode tableRow, string htmlContent, string type = "td")
    {
        var td = tableRow.OwnerDocument.CreateElement(type);
        td.InnerHtml = htmlContent;
        tableRow.AppendChild(td);
    }
}