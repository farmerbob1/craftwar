using System.Collections.Generic;
using Craftwar.NetServer.Db;

namespace Craftwar.NetServer.Protocol
{
    /// <summary>
    /// Business logic for friend requests/friendships — username resolution
    /// and the rules, no socket or wire-format knowledge (that's
    /// ControlProtocol and ClientConnection's job), same split as
    /// RatingService/AccountService. Takes AccountRepository directly
    /// (not AccountService) for the same reason RatingService does: this
    /// only needs username-&gt;id resolution, not login/session policy.
    /// </summary>
    public sealed class FriendsService
    {
        readonly AccountRepository _accounts;
        readonly FriendsRepository _friends;

        public FriendsService(AccountRepository accounts, FriendsRepository friends)
        {
            _accounts = accounts;
            _friends = friends;
        }

        public bool TryResolveAccount(string username, out long accountId)
        {
            if (_accounts.TryGetByUsername(username, out var account))
            {
                accountId = account.Id;
                return true;
            }
            accountId = 0;
            return false;
        }

        /// <summary>Null on success (this project's non-enum failure-reason
        /// convention — see ChannelManager.TryKick). If the target had
        /// already sent a request the other way, this completes the
        /// friendship immediately instead of leaving two pending requests
        /// around — a mutual request IS a friendship.</summary>
        public string SendRequest(long fromAccountId, string toUsername, out bool becameFriendsImmediately)
        {
            becameFriendsImmediately = false;
            if (!TryResolveAccount(toUsername, out long toId))
                return "no such user";
            if (toId == fromAccountId)
                return "cannot friend yourself";
            if (_friends.AreFriends(fromAccountId, toId))
                return "already friends";
            if (_friends.HasRequest(fromAccountId, toId))
                return "request already sent";

            if (_friends.HasRequest(toId, fromAccountId))
            {
                _friends.RemoveRequest(toId, fromAccountId);
                _friends.AddFriendship(fromAccountId, toId);
                becameFriendsImmediately = true;
                return null;
            }

            _friends.AddRequest(fromAccountId, toId);
            return null;
        }

        public string Respond(long responderAccountId, string requesterUsername, bool accept)
        {
            if (!TryResolveAccount(requesterUsername, out long requesterId))
                return "no such user";
            if (!_friends.HasRequest(requesterId, responderAccountId))
                return "no pending request from that user";

            _friends.RemoveRequest(requesterId, responderAccountId);
            if (accept)
                _friends.AddFriendship(responderAccountId, requesterId);
            return null;
        }

        public string RemoveFriend(long accountId, string friendUsername)
        {
            if (!TryResolveAccount(friendUsername, out long friendId))
                return "no such user";
            if (!_friends.AreFriends(accountId, friendId))
                return "not friends";
            _friends.RemoveFriendship(accountId, friendId);
            return null;
        }

        public List<(long accountId, string username)> ListFriends(long accountId) => _friends.ListFriends(accountId);
        public List<string> ListIncomingRequests(long accountId) => _friends.ListIncomingUsernames(accountId);
        public List<string> ListOutgoingRequests(long accountId) => _friends.ListOutgoingUsernames(accountId);
    }
}
