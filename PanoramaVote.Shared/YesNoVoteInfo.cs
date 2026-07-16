namespace PanoramaVote.Shared;

using System.Collections.Generic;

/// <summary>
///     Result payload handed to a <see cref="YesNoVoteResult" /> callback when a vote finishes.
/// </summary>
public sealed class YesNoVoteInfo
{
    /// <summary>Number of votes tallied in total.</summary>
    public int num_votes;

    /// <summary>Number of votes for yes.</summary>
    public int yes_votes;

    /// <summary>Number of votes for no.</summary>
    public int no_votes;

    /// <summary>Number of clients who were allowed to vote.</summary>
    public int num_clients;

    /// <summary>
    ///     Per-voter info keyed by voter index: value is (client slot, cast option).
    ///     Cast option <see cref="CastVote.VOTE_UNCAST" /> means the client did not vote.
    /// </summary>
    public Dictionary<int, (int Slot, int Item)> clientInfo = new();
}
