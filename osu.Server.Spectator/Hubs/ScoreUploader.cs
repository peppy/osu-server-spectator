// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Storage;

namespace osu.Server.Spectator.Hubs
{
    public class ScoreUploader : IEntityStore, IDisposable
    {
        /// <summary>
        /// Amount of time (in milliseconds) between checks for pending score uploads.
        /// </summary>
        public int UploadInterval { get; set; } = 50;

        private readonly ConcurrentQueue<UploadItem> queue = new ConcurrentQueue<UploadItem>();
        private readonly IDatabaseFactory databaseFactory;
        private readonly IScoreStorage scoreStorage;
        private readonly CancellationTokenSource cancellationSource;
        private readonly CancellationToken cancellationToken;

        public ScoreUploader(IDatabaseFactory databaseFactory, IScoreStorage scoreStorage)
        {
            this.databaseFactory = databaseFactory;
            this.scoreStorage = scoreStorage;

            cancellationSource = new CancellationTokenSource();
            cancellationToken = cancellationSource.Token;

            Task.Factory.StartNew(runFlushLoop, TaskCreationOptions.LongRunning);
        }

        private void runFlushLoop()
        {
            while (!queue.IsEmpty || !cancellationToken.IsCancellationRequested)
            {
                // ReSharper disable once MethodSupportsCancellation
                // We don't want flush to be cancelled as it needs to finish uploading.
                Flush().Wait();
                Thread.Sleep(UploadInterval);
            }
        }

        /// <summary>
        /// Enqueues a new score to be uploaded.
        /// </summary>
        /// <param name="scoreId">The score's ID.</param>
        /// <param name="score">The score.</param>
        public void Enqueue(long scoreId, Score score)
        {
            if (!AppSettings.SaveReplays)
                return;

            Interlocked.Increment(ref remainingUsages);

            queue.Enqueue(new UploadItem(scoreId, score));
        }

        /// <summary>
        /// Flushes all pending uploads.
        /// </summary>
        public async Task Flush()
        {
            try
            {
                if (queue.IsEmpty)
                    return;

                using (var db = databaseFactory.GetInstance())
                {
                    int countToTry = queue.Count;

                    for (int i = 0; i < countToTry; i++)
                    {
                        if (!queue.TryDequeue(out var item))
                            continue;

                        SoloScore? dbScore = await db.GetScoreFromId(item.ScoreId);

                        if (dbScore == null)
                        {
                            Console.WriteLine($"Score not found for ID: {item.ScoreId}");
                            return;
                        }

                        try
                        {
                            if (!dbScore.ScoreInfo.Passed)
                                return;

                            var updatedScore = new Score
                            {
                                Replay = item.Score.Replay,
                                ScoreInfo = dbScore.ScoreInfo.ToScoreInfo(item.Score.ScoreInfo.Mods, item.Score.ScoreInfo.BeatmapInfo)
                            };

                            // This needs to be updated to a valid reference for the score encoder.
                            updatedScore.ScoreInfo.Ruleset = item.Score.ScoreInfo.Ruleset;

                            await scoreStorage.WriteAsync(updatedScore);
                            await db.MarkScoreHasReplay(updatedScore);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref remainingUsages);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during score upload: {e}");
            }
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }

        private class UploadItem
        {
            public long ScoreId { get; }
            public Score Score { get; }

            public UploadItem(long scoreId, Score score)
            {
                ScoreId = scoreId;
                Score = score;
            }
        }

        private int remainingUsages;

        // Using the count of items in the queue isn't correct since items are dequeued for processing.
        public int RemainingUsages => remainingUsages;

        public string EntityName => "Score uploads";

        public void StopAcceptingEntities()
        {
            // Handled by the spectator hub.
        }
    }
}
