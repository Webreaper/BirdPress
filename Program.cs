
namespace BirdPress;

class Program
{
    static async Task Main(string[] args)
    {
        var birdPress = new BirdPress();
        await birdPress.Process();
    }
}
