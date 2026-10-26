using System;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Provider;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.ChallengeRuntime;

public sealed class ContainerNamingTests
{
    [Fact]
    public void Static_Flag_does_not_cause_different_learners_or_challenges_to_replace_each_others_containers()
    {
        var first = new ContainerConfig { Image = "test:latest", UserId = Guid.NewGuid(),
            TeamId = "first", ChallengeId = 1, Flag = "flag{static}" };
        var second = new ContainerConfig { Image = first.Image, UserId = Guid.NewGuid(),
            TeamId = "second", ChallengeId = 1, Flag = first.Flag };
        var third = new ContainerConfig { Image = first.Image, UserId = first.UserId,
            TeamId = first.TeamId, ChallengeId = 2, Flag = first.Flag };
        Assert.NotEqual(DockerMetadata.GetName(first), DockerMetadata.GetName(second));
        Assert.NotEqual(DockerMetadata.GetName(first), DockerMetadata.GetName(third));
    }
}
