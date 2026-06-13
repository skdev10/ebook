namespace EBookDashboard.Services.BookApi;

public static class BookApiConstants
{
    public const string DefaultUpstreamBaseUrl = "http://162.229.248.26:8001";

    public const string HttpClientNameShort = "BookApiShort";
    public const string HttpClientNameQueue = "BookApiQueue";
    public const string HttpClientNameLong = "BookApiLong";

    public static readonly string[] ValidSizes = ["1024x1024", "1536x1024", "1024x1536", "auto"];
    public static readonly string[] ValidQualities = ["low", "medium", "high", "auto"];
    public static readonly string[] ValidAudioExtensions = [".mp3", ".mp4", ".mpeg", ".mpga", ".m4a", ".wav", ".webm"];
}
