namespace PanoramaVote.Shared;

/// <summary>
///     Why a YES/NO vote ended.
/// </summary>
public enum YesNoVoteEndReason
{
    VoteEnd_AllVotes,  // All possible votes were cast
    VoteEnd_TimeUp,    // Time ran out
    VoteEnd_Cancelled, // The vote got cancelled
}
