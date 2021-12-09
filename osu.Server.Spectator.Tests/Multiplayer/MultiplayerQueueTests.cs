// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task AddNonExistentBeatmap()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync((string?)null);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum"
            }));
        }

        [Fact]
        public async Task AddCustomizedBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(9999)).ReturnsAsync("correct checksum");

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 9999,
                BeatmapChecksum = "incorrect checksum",
            }));
        }

        [Theory]
        [InlineData(ILegacyRuleset.MAX_LEGACY_RULESET_ID + 1)]
        [InlineData(-1)]
        public async Task AddCustomRulesetThrows(int rulesetID)
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetID
            }));
        }

        [Fact]
        public async Task RoomStartsWithCurrentPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1, room.Settings.PlaylistItemId);
            }
        }

        [Fact]
        public async Task RoomStartsWithCorrectQueueingMode()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");
            Database.Setup(db => db.GetRoomAsync(ROOM_ID))
                    .Callback<long>(InitialiseRoom)
                    .ReturnsAsync(() => new multiplayer_room
                    {
                        type = database_match_type.head_to_head,
                        ends_at = DateTimeOffset.Now.AddMinutes(5),
                        user_id = USER_ID,
                        queue_mode = database_queue_mode.all_players
                    });

            await Hub.JoinRoom(ROOM_ID);

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, (await Database.Object.GetAllPlaylistItemsAsync(ROOM_ID)).Length);
            }
        }

        [Fact]
        public async Task JoinedRoomContainsAllPlaylistItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            InitialiseRoom(ROOM_ID);

            await Database.Object.AddPlaylistItemAsync(new multiplayer_playlist_item(ROOM_ID, new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            }));

            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(2, room.Playlist.Count);
                Assert.Equal(1234, room.Playlist[0].BeatmapID);
                Assert.Equal(3333, room.Playlist[1].BeatmapID);
            }
        }

        [Fact]
        public async Task UsersCanRemoveTheirOwnItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.RemovePlaylistItem(2);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1, room.Playlist.Count);
                Database.Verify(db => db.RemovePlaylistItemAsync(ROOM_ID, 2), Times.Once);
                Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
            }
        }

        [Fact]
        public async Task UsersCanNotRemoveOtherUsersItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser2);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(2));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task HostCanRemoveOtherUsersItems()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            SetUserContext(ContextUser2);
            await Hub.JoinRoom(ROOM_ID);
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            SetUserContext(ContextUser);
            await Hub.RemovePlaylistItem(2);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(1, room.Playlist.Count);
                Database.Verify(db => db.RemovePlaylistItemAsync(ROOM_ID, 2), Times.Once);
                Receiver.Verify(client => client.PlaylistItemRemoved(2), Times.Once);
            }
        }

        [Fact]
        public async Task ExternalItemsCanNotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(3));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task CurrentItemCanNotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });
            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));
        }

        [Fact]
        public async Task ExpiredItemsCanNotBeRemoved()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync("3333");

            await Hub.JoinRoom(ROOM_ID);
            await Hub.ChangeSettings(new MultiplayerRoomSettings { QueueMode = QueueMode.AllPlayers });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.AddPlaylistItem(new MultiplayerPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "3333"
            });

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
            await Hub.ChangeState(MultiplayerUserState.Idle);

            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.RemovePlaylistItem(1));
            Database.Verify(db => db.RemovePlaylistItemAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            Receiver.Verify(client => client.PlaylistItemRemoved(It.IsAny<long>()), Times.Never);
        }
    }
}