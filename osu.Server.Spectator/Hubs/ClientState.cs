// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.Spectator.Hubs
{
    [Serializable]
    public sealed class ClientState<TUserState>
        where TUserState : class
    {
        public TUserState? UserState { get; private set; }

        /// <summary>
        /// The owner of this state.
        /// </summary>
        public string ConnectionId { get; set; }

        public ClientState(in string connectionId, in TUserState? userState)
        {
            this.UserState = userState;
            ConnectionId = connectionId;
        }

        public void SetUserState(TUserState? state)
        {
            UserState = state;
        }

        /// <summary>
        /// Mark this client as invalid. After called, the client should not be allowed to perform any operations.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Invalidate()
        {
            throw new NotImplementedException();
        }
    }
}
