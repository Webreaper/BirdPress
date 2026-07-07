namespace BirdPress;

public record Species(
    string scientific_name,
    string common_name,
    int count,
    string thumbnail_url,
    DateTime first_heard,
    DateTime last_heard,
    double avg_confidence,
    double max_confidence)
{
    public override string ToString()
    {
        return $"{common_name} Last Heard: {last_heard}, Thumbnail: {thumbnail_url}";
    } 
}

public record BirdPressSettings(
    string birdNetUrl, 
    string wordpressBaseUrl, 
    string wordpressUser, 
    string wordpressPassword, 
    int wordpressPostId,
    double minThreshold = 0.7);