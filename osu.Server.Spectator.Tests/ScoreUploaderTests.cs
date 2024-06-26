// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Storage;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class ScoreUploaderTests
    {
        private readonly Mock<IDatabaseAccess> mockDatabase;
        private readonly Mock<IScoreStorage> mockStorage;
        private readonly Mock<IDatabaseFactory> databaseFactory;
        private readonly Mock<ILoggerFactory> loggerFactory;

        public ScoreUploaderTests()
        {
            mockDatabase = new Mock<IDatabaseAccess>();
            mockDatabase.Setup(db => db.GetScoreFromToken(1)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 2,
                passed = true
            }));

            databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);

            loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                         .Returns(new Mock<ILogger>().Object);

            mockStorage = new Mock<IScoreStorage>();
        }

        /// <summary>
        /// Currently the replay upload process deals with two sources of score data.
        /// One is local to the spectator server and created in `SpectatorHub.BeginPlaySession()`.
        /// Among others, it contains the username of the player, which in the legacy replay format
        /// is the only piece of information that links the replay to the player.
        /// The other source is the database. This source will contain the online ID and passed state of the score,
        /// which will not be available in the local instance.
        /// This test ensures that the two are merged correctly in order to not drop any important data.
        /// </summary>
        [Fact]
        public async Task ScoreDataMergedCorrectly()
        {
            enableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            await uploader.EnqueueAsync(1, new Score
            {
                ScoreInfo =
                {
                    User = new APIUser
                    {
                        Id = 1234,
                        Username = "some user",
                    }
                    // note OnlineID and Passed not set.
                }
            });

            await uploadsCompleteAsync(uploader);

            mockStorage.Verify(s => s.WriteAsync(
                It.Is<Score>(score => score.ScoreInfo.OnlineID == 2
                                      && score.ScoreInfo.Passed
                                      && score.ScoreInfo.User.Username == "some user")), Times.Once);
        }

        [Fact]
        public async Task ScoreUploads()
        {
            enableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            await uploader.EnqueueAsync(1, new Score());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 2)), Times.Once);

            await uploader.EnqueueAsync(1, new Score());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 2)), Times.Exactly(2));
        }

        [Fact]
        public async Task ScoreDoesNotUploadIfDisabled()
        {
            disableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            await uploader.EnqueueAsync(1, new Score());
            await Task.Delay(1000);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Never);
        }

        [Fact]
        public async Task ScoreUploadsWithDelayedScoreToken()
        {
            enableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            // Score with no token.
            await uploader.EnqueueAsync(2, new Score());
            await Task.Delay(1000);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Never);

            // Give the score a token.
            mockDatabase.Setup(db => db.GetScoreFromToken(2)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 3,
                passed = true
            }));

            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 3)), Times.Once);
        }

        [Fact]
        public async Task TimedOutScoreDoesNotUpload()
        {
            enableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            uploader.TimeoutInterval = 0;

            // Score with no token.
            await uploader.EnqueueAsync(2, new Score());
            Thread.Sleep(1000); // Wait for cancellation.
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Never);

            // Give the score a token now. It should still not upload because it has timed out.
            mockDatabase.Setup(db => db.GetScoreFromToken(2)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 3,
                passed = true
            }));
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Never);

            // New score that has a token (ensure the loop keeps running).
            mockDatabase.Setup(db => db.GetScoreFromToken(3)).Returns(Task.FromResult<SoloScore?>(new SoloScore
            {
                id = 4,
                passed = true
            }));
            await uploader.EnqueueAsync(3, new Score());
            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Once);
            mockStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 4)), Times.Once);
        }

        [Fact]
        public async Task FailedScoreHandledGracefully()
        {
            enableUpload();
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            bool shouldThrow = true;
            int uploadCount = 0;

            mockStorage.Setup(storage => storage.WriteAsync(It.IsAny<Score>()))
                       .Callback<Score>(_ =>
                       {
                           // ReSharper disable once AccessToModifiedClosure
                           if (shouldThrow)
                               throw new InvalidOperationException();

                           uploadCount++;
                       });

            // Throwing score.
            await uploader.EnqueueAsync(1, new Score());
            await uploadsCompleteAsync(uploader);
            Assert.Equal(0, uploadCount);

            shouldThrow = false;

            // Same score shouldn't reupload.
            await Task.Delay(1000);
            Assert.Equal(0, uploadCount);

            await uploader.EnqueueAsync(1, new Score());
            await uploadsCompleteAsync(uploader);
            Assert.Equal(1, uploadCount);
        }

        [Fact]
        public async Task TestMassUploads()
        {
            enableUpload();
            AppSettings.ReplayUploaderConcurrency = 4;
            var uploader = new ScoreUploader(loggerFactory.Object, databaseFactory.Object, mockStorage.Object);

            for (int i = 0; i < 1000; ++i)
                await uploader.EnqueueAsync(1, new Score());

            await uploadsCompleteAsync(uploader);
            mockStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 2)), Times.Exactly(1000));
            AppSettings.ReplayUploaderConcurrency = 1;
        }

        private void enableUpload() => AppSettings.SaveReplays = true;
        private void disableUpload() => AppSettings.SaveReplays = false;

        private async Task uploadsCompleteAsync(ScoreUploader uploader, int attempts = 5)
        {
            while (uploader.RemainingUsages > 0)
            {
                if (attempts <= 0)
                    Assert.Fail("Waiting for score upload to proceed timed out");

                attempts -= 1;
                await Task.Delay(1000);
            }
        }
    }
}
