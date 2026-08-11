using Tauri.Core.Helpers;

namespace Tauri.Core.Tests;

public sealed class CharacterHelpersTests
{
    [Theory]
    [InlineData("evermoon", "[EN] Evermoon", "Evermoon")]
    [InlineData(" [HU] Tauri WoW Server ", "[HU] Tauri WoW Server", "Tauri")]
    [InlineData("WOD", "[HU] Warriors of Darkness", "WoD")]
    public void TryResolveRealm_KnownAlias_NormalizesRealm(
        string input,
        string expectedApiRealm,
        string expectedDisplayRealm
    )
    {
        var succeeded = CharacterHelpers.TryResolveRealm(input, out var apiRealm, out var realm);

        Assert.True(succeeded);
        Assert.Equal(expectedApiRealm, apiRealm);
        Assert.Equal(expectedDisplayRealm, realm);
    }

    [Fact]
    public void TryResolveRealm_UnknownRealm_ReturnsFailureAndEmptyOutputs()
    {
        var succeeded = CharacterHelpers.TryResolveRealm(
            "Unknown",
            out var apiRealm,
            out var realm
        );

        Assert.False(succeeded);
        Assert.Empty(apiRealm);
        Assert.Empty(realm);
    }

    [Fact]
    public void TryExtractCharacterWithRealm_CommentLine_ReturnsFailure()
    {
        var succeeded = CharacterHelpers.TryExtractCharacterWithRealm(
            "# Example-Evermoon",
            out _,
            out _,
            out _
        );

        Assert.False(succeeded);
    }
}
