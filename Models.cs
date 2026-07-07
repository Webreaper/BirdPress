namespace BirdPress;

public record Species( 
    string scientific_name, 
    string common_name,
    int count, 
    string thumbnail_url, 
    DateTime first_heard,
    DateTime last_heard,
    double avg_confidence,
    double max_confidence);

public record BirdPressSettings(
    string birdNetUrl, 
    string wordpressBaseUrl, 
    string wordpressUser, 
    string wordpressPassword, 
    int wordpressPostId,
    double minThreshold = 0.7);