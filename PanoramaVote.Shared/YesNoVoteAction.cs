namespace PanoramaVote.Shared;

/// <summary>
///     Menu-action callbacks raised through <see cref="YesNoVoteHandler" />.
/// </summary>
public enum YesNoVoteAction
{
    VoteAction_Start,  // nothing passed
    VoteAction_Vote,   // param1 = client slot, param2 = choice (VOTE_OPTION1 = yes, VOTE_OPTION2 = no)
    VoteAction_End,    // param1 = YesNoVoteEndReason reason why the vote ended
}
