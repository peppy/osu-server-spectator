// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public abstract class StatefulUserHub<TClient, TUserState> : Hub<TClient>
        where TUserState : class
        where TClient : class
    {
        private static readonly Dictionary<int, TUserState> active_states = new Dictionary<int, TUserState>();

        protected StatefulUserHub(IDistributedCache cache)
        {
        }

        protected static KeyValuePair<int, TUserState>[] GetAllStates()
        {
            lock (active_states)
                return active_states.ToArray();
        }

        /// <summary>
        /// The osu! user id for the currently processing context.
        /// </summary>
        protected int CurrentContextUserId => int.Parse(Context.UserIdentifier);

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"User {CurrentContextUserId} connected!");

            await cleanupState(false);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a user's previous state is no longer valid.
        /// </summary>
        /// <param name="state">The last user state. May be null. This is automatically cleared on disconnection.</param>
        protected virtual Task CleanupPreviousState(TUserState state) => Task.CompletedTask;

        public sealed override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {CurrentContextUserId} disconnected!");

            await cleanupState(true);

            await base.OnDisconnectedAsync(exception);
        }

        private async Task cleanupState(bool isDisconnect)
        {
            var state = await GetLocalUserState();

            if (state == null) return;

            if (state is ClientState clientState)
            {
                if (isDisconnect)
                {
                    // if this is a disconnection, we only want to clean the state if it is our own.
                    if (clientState.ConnectionId == Context.ConnectionId)
                        await runCleanup();
                }
                else
                {
                    // in another scenario, we are looking to clear a state that is NOT our own.
                    if (clientState.ConnectionId != Context.ConnectionId)
                        await runCleanup();
                }
            }
            else
            {
                // for cases the client state doesn't have a connection id, we cannot be sure of the owner so should just nuke it.
                // this case can be removed once spectator hub is reworked to use ClientState as a base class.
                await runCleanup();
            }

            async Task runCleanup()
            {
                await CleanupPreviousState(state);
                await RemoveLocalUserState();
            }
        }

        protected Task UpdateLocalUserState(TUserState state)
        {
            lock (active_states)
                active_states[CurrentContextUserId] = state;

            return Task.CompletedTask;
        }

        protected Task<TUserState?> GetLocalUserState() => GetStateFromUser(CurrentContextUserId);

        protected Task RemoveLocalUserState()
        {
            lock (active_states)
                active_states.Remove(CurrentContextUserId);

            return Task.CompletedTask;
        }

        protected Task<TUserState?> GetStateFromUser(int userId)
        {
            TUserState? state;

            lock (active_states)
                active_states.TryGetValue(userId, out state);

            return Task.FromResult(state);
        }

        public static string GetStateId(int userId) => $"state-{typeof(TClient)}:{userId}";

        public static void Reset()
        {
            lock (active_states)
                active_states.Clear();
        }
    }
}
