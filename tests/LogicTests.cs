using FM27AbilityNumbers;
using Xunit;

public class ClassifyTests
{
    [Theory]
    // Real paths taken from FM27's UI templates and table factories.
    [InlineData(AbilityKind.Current, "minvalue", "CurrentAbilityStarRange")]
    [InlineData(AbilityKind.Potential, "maxvalue", "PotentialAbilityStarRange")]
    [InlineData(AbilityKind.Current, "minvalue", "StarRange", "ScoutedCurrentAbilityInfo", "PlayerReportForContextTeam")]
    [InlineData(AbilityKind.Potential, "maxvalue", "StarRange", "ScoutedPotentialAbilityInfo", "ClubCoachReport")]
    [InlineData(AbilityKind.Current, "maxvalue", "coachcurrentabilitystarrange")]
    [InlineData(AbilityKind.Potential, "maxvalue", "scoutpotentialabilitystarrange")]
    [InlineData(AbilityKind.None, "maxvalue", "CurrentInPossessionRoleStarRange")]
    [InlineData(AbilityKind.None, "maxvalue", "CurrentRoleStarRange")]
    [InlineData(AbilityKind.None, "minvalue", "playerworldreputationstarrating")]
    [InlineData(AbilityKind.None, "minvalue", "FacilityStarRange", "YouthFacilityRating")]
    [InlineData(AbilityKind.None)]
    // Role ratings (PlayerRoleCurrentAbilityRow / PlayerRoleAbilityRatingCell): the player's ability in a role, not CA.
    [InlineData(AbilityKind.None, "maxvalue", "currentabilitystars", "role")]
    [InlineData(AbilityKind.None, "minvalue", "currentabilitystars", "binding")]
    [InlineData(AbilityKind.None, "maxvalue", "PotentialAbilityStars")]
    [InlineData(AbilityKind.None, "maxvalue", "ComparisonPlayerCurrentAbilityComparision")]
    public void Classifies(AbilityKind expected, params string[] names) =>
        Assert.Equal(expected, Logic.Classify(names));

    [Fact]
    public void LeafMostMatchWins() =>
        Assert.Equal(AbilityKind.Potential, Logic.Classify(new[] { "PotentialAbilityStarRange", "ScoutedCurrentAbilityInfo" }));

    [Fact]
    public void NullNamesIgnored() =>
        Assert.Equal(AbilityKind.Current, Logic.Classify(new string[] { null, "", "CurrentAbilityStarRange" }));
}

public class FormatTests
{
    [Theory]
    [InlineData(143, "143")]
    [InlineData(200, "200")]
    [InlineData(1, "1")]
    [InlineData(-95, "-95")]
    [InlineData(0, null)]
    [InlineData(201, null)]
    [InlineData(-201, null)]
    [InlineData(short.MaxValue, null)]
    public void Formats(int raw, string expected) => Assert.Equal(expected, Logic.FormatAbility(raw));
}

public class SortPathTests
{
    const string CA = "PlayerCurrentAbility", PA = "PlayerPotentialAbility";

    [Theory]
    [InlineData("binding.currentabilitystarrange.maxvalue", "binding.PlayerCurrentAbility")]
    [InlineData("binding.potentialabilitystarrange.maxvalue", "binding.PlayerPotentialAbility")]
    [InlineData("binding.coachcurrentabilitystarrange.maxvalue", "binding.PlayerCurrentAbility")]
    [InlineData("binding.coachpotentialabilitystarrange.maxvalue", "binding.PlayerPotentialAbility")]
    [InlineData("binding.ScoutCurrentAbilityStarRange.MaxValue", "binding.PlayerCurrentAbility")]
    [InlineData("binding.ScoutPotentialAbilityStarRange.MaxValue", "binding.PlayerPotentialAbility")]
    [InlineData("binding.player.CurrentAbilityStarRange.maxvalue", "binding.player.PlayerCurrentAbility")]
    [InlineData("binding.BestPlayer.CurrentAbilityStarRange.minvalue", "binding.BestPlayer.PlayerCurrentAbility")]
    public void Rewrites(string path, string expected)
    {
        Assert.True(Logic.TryRewriteSortPath(path, CA, PA, out var r));
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData("binding.CurrentRoleStarRange.MaxValue")]
    [InlineData("binding.CurrentInPossessionRoleStarRange.MaxValue")]
    [InlineData("binding.playerworldreputationstarrating.minvalue")]
    [InlineData("binding.InPossessionRoleStarRangeForSquadPlannerSeason.MaxValue")]
    [InlineData("binding.currentabilitystarrange")]
    [InlineData("binding.Age")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesOtherPathsAlone(string path) =>
        Assert.False(Logic.TryRewriteSortPath(path, CA, PA, out _));

    [Fact]
    public void EmptyPropertyMeansNoRewrite() =>
        Assert.False(Logic.TryRewriteSortPath("binding.currentabilitystarrange.maxvalue", "", PA, out _));
}

public class ClassesTests
{
    [Fact]
    public void SplitsOnSpaceCommaSemicolon() =>
        Assert.Equal(new[] { "body-xs-strong", "global-text-primary", "x" }, Logic.SplitClasses(" body-xs-strong, global-text-primary;x "));
}

public class PairTests
{
    [Theory]
    [InlineData(158, 170, true, true, "CA 158 · PA 170")]
    [InlineData(158, 170, true, false, "CA 158")]
    [InlineData(158, 170, false, true, "PA 170")]
    [InlineData(158, 170, false, false, null)]
    [InlineData(0, 170, true, true, "PA 170")]
    [InlineData(158, -95, true, true, "CA 158 · PA -95")]
    [InlineData(0, 0, true, true, null)]
    public void Formats(int ca, int pa, bool showCa, bool showPa, string expected) =>
        Assert.Equal(expected, Logic.FormatPair(ca, pa, showCa, showPa));
}
