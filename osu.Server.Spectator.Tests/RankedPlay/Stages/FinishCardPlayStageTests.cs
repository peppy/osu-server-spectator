// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.Rooms;
using Xunit;

namespace osu.Server.Spectator.Tests.RankedPlay.Stages
{
    public class FinishCardPlayStageTests : RankedPlayStageImplementationTest
    {
        public FinishCardPlayStageTests()
            : base(RankedPlayStage.FinishCardPlay)
        {
        }

        [Fact]
        public void UsersUnreadiedOnEnter()
        {
            Assert.Equal(MultiplayerUserState.Idle, Room.Users[0].State);
            Assert.Equal(BeatmapAvailability.Unknown().State, Room.Users[0].BeatmapAvailability.State);
        }

        [Fact]
        public async Task ContinuesToGameplayWarmupWhenAllPlayersReady()
        {
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            SetUserContext(ContextUser2);
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            Assert.Equal(RankedPlayStage.GameplayWarmup, RoomState.Stage);
        }

        [Fact]
        public async Task ContinuesToEndedWhenAnyPlayerLeaves()
        {
            await Hub.LeaveRoom();

            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);
            Assert.Equal(0, UserState.Life);
        }

        [Fact]
        public async Task ContinuesToNextRoundWhenAnyPlayerFailsToBecomeReady()
        {
            await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
            Assert.Equal(RankedPlayStage.FinishCardPlay, RoomState.Stage);

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);

            Assert.Equal(1_000_000, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }

        [Fact]
        public async Task ContinuesToNextRoundWhenAllPlayersFailToBecomeReady()
        {
            await FinishCountdown();
            Assert.Equal(RankedPlayStage.CardPlay, RoomState.Stage);

            Assert.Equal(900_000, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }

        [Fact]
        public async Task ContinuesToEndedWhenPlayerDiesFromFailingToBecomeReady()
        {
            RoomState.Users[USER_ID].Life = 50_000;

            await FinishCountdown();
            Assert.Equal(RankedPlayStage.Ended, RoomState.Stage);

            Assert.Equal(0, RoomState.Users[USER_ID].Life);
            Assert.Equal(900_000, RoomState.Users[USER_ID_2].Life);
        }
    }
}
