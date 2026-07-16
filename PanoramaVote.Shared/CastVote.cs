namespace PanoramaVote.Shared;

/// <summary>
///     A cast vote option, mirroring the engine's vote-controller slot values.
/// </summary>
public enum CastVote
{
    VOTE_NOTINCLUDED = -1,
    VOTE_OPTION1,      // Yes
    VOTE_OPTION2,      // No
    VOTE_OPTION3,
    VOTE_OPTION4,
    VOTE_OPTION5,
    VOTE_UNCAST = 5,
}
