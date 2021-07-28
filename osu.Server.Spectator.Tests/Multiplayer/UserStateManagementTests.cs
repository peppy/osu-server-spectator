// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using System.Linq;
using Moq;
using osu.Game.Online.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class UserStateManagementTests : MultiplayerTest
    {
        [Fact]
        public async Task UserStateChangeNotifiesOtherUsers()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeState(MultiplayerUserState.Ready);
            Receiver.Verify(r => r.UserStateChanged(USER_ID, MultiplayerUserState.Ready), Times.Once);
        }

        [Theory]
        [InlineData(MultiplayerUserState.WaitingForLoad)]
        [InlineData(MultiplayerUserState.Playing)]
        [InlineData(MultiplayerUserState.Results)]
        public async Task UserCantChangeStateToReservedStates(MultiplayerUserState reservedState)
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateChangeException>(() => Hub.ChangeState(reservedState));
        }

        [Fact]
        public async Task StartingMatchWithNoReadyUsersFails()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task StartingMatchWithHostNotReadyFails()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task StartingAlreadyStartedMatchFails()
        {
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);

            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.StartMatch());
        }

        [Fact]
        public async Task AllUsersBackingOutFromLoadCancelsTransitionToPlay()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

            await Hub.ChangeState(MultiplayerUserState.Idle);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
        }

        [Fact]
        public async Task OnlyReadiedUpUsersTransitionToPlay()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);

                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.WaitingForLoad);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }

            await Hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Playing);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }
        }

        [Fact]
        public async Task UserDisconnectsDuringGameplayUpdatesRoomState()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser);
            await Hub.StartMatch();

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Equal(MultiplayerRoomState.WaitingForLoad, room.Item?.State);
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.WaitingForLoad, u.State));
            }

            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            SetUserContext(ContextUser2);
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Playing, u.State));
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);
            }

            // first user exits gameplay
            SetUserContext(ContextUser);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Playing, room.Item?.State);

            // second user gets disconnected
            SetUserContext(ContextUser2);
            await Hub.LeaveRoom();

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
        }

        [Fact]
        public async Task OnlyFinishedUsersTransitionToResults()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);

            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            VerifyRemovedFromGameplayGroup(ContextUser, ROOM_ID);
            VerifyRemovedFromGameplayGroup(ContextUser2, ROOM_ID, false);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Results);
                Assert.Single(room.Item?.Users, u => u.State == MultiplayerUserState.Idle);
            }
        }

        [Fact]
        public async Task OnlyReadyPlayersAreAddedToAndRemovedFromGameplayGroup()
        {
            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeState(MultiplayerUserState.Ready);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);

            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);

            VerifyAddedToGameplayGroup(ContextUser, ROOM_ID);
            VerifyAddedToGameplayGroup(ContextUser2, ROOM_ID, false);

            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            VerifyRemovedFromGameplayGroup(ContextUser, ROOM_ID);
            VerifyRemovedFromGameplayGroup(ContextUser2, ROOM_ID, false);
        }

        [Fact]
        public async void NotReadyUsersDontGetLoadRequest()
        {
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);

            SetUserContext(ContextUser);

            using (var room = await Rooms.GetForUse(ROOM_ID))
                Assert.All(room.Item?.Users, u => Assert.Equal(MultiplayerUserState.Idle, u.State));

            // one user enters a ready state.
            await Hub.ChangeState(MultiplayerUserState.Ready);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Idle));
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Ready));

                Assert.Equal(MultiplayerRoomState.Open, room.Item?.State);
            }

            // host requests the start of the match.
            await Hub.StartMatch();

            GameplayReceiver.Verify(r => r.LoadRequested(), Times.Once);
            Receiver.Verify(r => r.LoadRequested(), Times.Never);

            using (var room = await Rooms.GetForUse(ROOM_ID))
            {
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.WaitingForLoad));
                Assert.Single(room.Item?.Users.Where(u => u.State == MultiplayerUserState.Idle));
            }
        }
    }
}