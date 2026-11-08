namespace GZCTF.Features.Leaderboard.Domain;

// Append-only administrative journal. Compensation entries preserve the original import baseline.
public sealed class RankingOperation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OperationId { get; set; }
    public string PayloadHash { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? DetailsJson { get; set; }
    public long Delta { get; set; }
    public long BeforeScore { get; set; }
    public long AfterScore { get; set; }
    public int SolvedDelta { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? ReversesId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class RankingUserState
{
    public Guid UserId { get; set; }
    public long Revision { get; set; }
    public bool? Hidden { get; set; }
}
public sealed class RankingSolveCorrection
{
    public Guid UserId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? ImportedScoreBaselineId { get; set; }
    public int FrozenScore { get; set; }
    public DateTimeOffset OriginalSolvedAtUtc { get; set; }
    public string? AttributionMetadataJson { get; set; }
    public bool Revoked { get; set; }
}
public sealed class RankingLevelSettings
{
    public int Id { get; set; } = 1;
    public long Revision { get; set; }
    public string ThresholdsJson { get; set; } = "[0,1000,3000,6000,10000,15000,22000,30000,40000,50000]";
}

public sealed class RankingScoreEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid ChallengeId { get; set; }
    public long ScoreDelta { get; set; }
    public int SolvedDelta { get; set; }
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
}
