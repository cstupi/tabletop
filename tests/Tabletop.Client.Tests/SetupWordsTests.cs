using Tabletop.Client.Core; using Tabletop.Geometry;
namespace Tabletop.Client.Tests;

public class SetupWordsTests
{
    [Fact]
    public void Textures_and_zone_styles_read_as_the_browser_names_them()
    {
        Assert.Equal(("Grass", "Large forest"), (SetupWords.Texture("grass"), SetupWords.Name("large-forest")));
        Assert.Equal(new[] { "Frontline", "Ground War", "Side Battle", "Disordered", "Spearhead", "Opposing Forces", "Free-for-all" }, ZoneStyles.All.Select(SetupWords.ZoneStyle));
    }
}
