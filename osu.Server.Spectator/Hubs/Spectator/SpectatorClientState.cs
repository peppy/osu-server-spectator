// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;

namespace osu.Server.Spectator.Hubs.Spectator
{
    [Serializable]
    public class SpectatorClientState : ClientState
    {
        /// <summary>
        /// When a user is in gameplay, this is the state as conveyed at the start of the play session.
        /// </summary>
        public SpectatorState? State;

        /// <summary>
        /// When a user is in gameplay, this is the imminent score. It will be updated throughout a play session.
        /// </summary>
        public Score? Score;

        /// <summary>
        /// The score token as conveyed by the client at the beginning of a play session.
        /// </summary>
        public long? ScoreToken;

        [JsonConstructor]
        public SpectatorClientState(in string connectionId, in int userId)
            : base(connectionId, userId)
        {
        }
    }
}
