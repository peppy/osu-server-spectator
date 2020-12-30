// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private static readonly Dictionary<int, ClientState<TUserState>> connected_users = new Dictionary<int, ClientState<TUserState>>();

        protected StatefulUserHub(IDistributedCache cache)
        {
        }

        protected static KeyValuePair<int, TUserState>[] GetAllStates()
        {
            lock (connected_users)
            {
                return connected_users
                       .Where(kvp => kvp.Value.UserState != null)
                       .Select(state =>
                       {
                           Debug.Assert(state.Value.UserState != null);
                           return new KeyValuePair<int, TUserState>(state.Key, state.Value.UserState);
                       }).ToArray();
            }
        }

        /// <summary>
        /// The osu! user id for the currently processing context.
        /// </summary>
        protected int CurrentContextUserId => int.Parse(Context.UserIdentifier);

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"User {CurrentContextUserId} connected!");

            // return this user to a fresh state by destroying any associated state, regardless of the connection.
            await ClearLocalUserState();

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

            await ClearLocalUserState();

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Update an associated user state. Setting to null clears the state.
        /// </summary>
        protected void UpdateLocalUserState(TUserState? state)
        {
            lock (connected_users)
            {
                if (connected_users.TryGetValue(CurrentContextUserId, out var clientState))
                    clientState.SetUserState(state);
            }
        }

        protected TUserState? GetLocalUserState() => GetStateFromUser(CurrentContextUserId);

        /// <summary>
        /// Remove any state for the current user across *all connections*.
        /// This can be used to achieve a consistent state on connection or disconnection.
        /// </summary>
        /// <param name="createFresh">Whether a fresh state should be created after clearing any existing state. If true, it is guaranteed that the local connection holds the active state after completion.</param>
        protected Task ClearLocalUserState(bool createFresh = false)
        {
            while (true)
            {
                ClientState<TUserState>? clientState;

                lock (connected_users)
                {
                    connected_users.TryGetValue(CurrentContextUserId, out clientState);

                    clientState.Invalidate();
                }

                if (clientState != null)
                {
                    {
                        // there is an existing state
                        // we need to clean it up.
                    }
                }

                connected_users.Remove(CurrentContextUserId);
            }

            // todo: unbreak
            // return CleanupPreviousState(state);
            return Task.CompletedTask;
        }

        protected TUserState? GetStateFromUser(int userId)
        {
            // todo: unbreak
            // lock (connected_users)
            // {
            //     if (connected_users.TryGetValue(userId, out var clientState))
            //         return clientState.State;
            // }

            return null;
        }

        public static string GetStateId(int userId) => $"state-{typeof(TClient)}:{userId}";

        public static void Reset()
        {
            lock (connected_users)
                connected_users.Clear();
        }
    }
}
