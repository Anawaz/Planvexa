namespace Planvexa.UnitTests.Tenancy;

using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Acme", "acme")]
    [InlineData("acme-corp", "acme-corp")]
    [InlineData("  ACME  ", "acme")]
    public void NormalizeSlug_accepts_valid_slugs(string input, string expected)
        => Workspace.NormalizeSlug(input).ShouldBe(expected);

    [Theory]
    [InlineData("a")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ACME CORP")]
    [InlineData("acme_corp")]
    public void NormalizeSlug_rejects_invalid_slugs(string input)
        => Should.Throw<ArgumentException>(() => Workspace.NormalizeSlug(input));

    [Theory]
    [InlineData("My Workspace", "my-workspace")]
    [InlineData("R&D Team!!", "r-d-team")]
    [InlineData("   ", "main")]
    public void SlugGenerator_produces_valid_slugs(string input, string expected)
        => SlugGenerator.Generate(input, "main").ShouldBe(expected);
}
