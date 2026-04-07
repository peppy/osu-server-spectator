// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue
{
    public class MatchmakingLobby
    {
        /// <summary>
        /// Retrieves the matchmaking queue for a given pool ID.
        /// </summary>
        public required Func<int, MatchmakingQueue?> LookupQueue { get; init; }

        private readonly int poolId;
        private readonly IHubContext<MultiplayerHub> hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly string groupName;

        public MatchmakingLobby(int poolId, IHubContext<MultiplayerHub> hub, IDatabaseFactory dbFactory)
        {
            this.poolId = poolId;
            this.hub = hub;
            this.dbFactory = dbFactory;

            groupName = $"matchmaking-lobby-users:{poolId}";
        }

        public async Task Add(MultiplayerClientState state)
        {
            await hub.Groups.AddToGroupAsync(state.ConnectionId, groupName);
            await hub.Clients.Client(state.ConnectionId).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), await buildStatusUpdate(state.UserId));
        }

        public async Task Remove(MultiplayerClientState state)
        {
            await hub.Groups.RemoveFromGroupAsync(state.ConnectionId, groupName);
        }

        public async Task Update()
        {
            await hub.Clients.Group(groupName).SendAsync(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), await buildStatusUpdate(null));
        }

        public Task RecordMatch(MatchRoomState state)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a status update for the lobby to be distributed to clients.
        /// </summary>
        /// <param name="targetUserId">The target user's whose rating is to be included in the distribution.</param>
        /// <returns>The status update bundle.</returns>
        private async Task<MatchmakingLobbyStatus> buildStatusUpdate(int? targetUserId)
        {
            MatchmakingQueue? queue = LookupQueue(poolId);
            MatchmakingQueueUser[] queuedUsers = queue?.GetAllUsers() ?? [];
            Random.Shared.Shuffle(queuedUsers);

            return new MatchmakingLobbyStatus
            {
                UsersInQueue = queuedUsers.Take(50).Select(u => u.UserId).ToArray(),
            };
        }
    }
}
