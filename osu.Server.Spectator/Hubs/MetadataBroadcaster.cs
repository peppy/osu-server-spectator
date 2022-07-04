// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Timers;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Metadata;
using osu.Server.Spectator.Database;

namespace osu.Server.Spectator.Hubs;

/// <summary>
/// A service which broadcasts any new metadata changes to <see cref="MetadataHub"/>.
/// </summary>
public class MetadataBroadcaster : IDisposable
{
    private readonly IDatabaseFactory databaseFactory;
    private readonly IHubContext<MetadataHub> metadataHubContext;

    private readonly Timer timer;

    private uint? lastQueueId;

    public MetadataBroadcaster(IDatabaseFactory databaseFactory, IHubContext<MetadataHub> metadataHubContext)
    {
        this.databaseFactory = databaseFactory;
        this.metadataHubContext = metadataHubContext;

        timer = new Timer(5000);
        timer.AutoReset = false;
        timer.Elapsed += pollForChanges;
        timer.Start();
    }

    private async void pollForChanges(object? sender, ElapsedEventArgs args)
    {
        try
        {
            using (var db = databaseFactory.GetInstance())
            {
                var updates = await db.GetUpdatedBeatmapSets(lastQueueId);

                lastQueueId = updates.LastProcessedQueueID;
                Console.WriteLine($"Polled beatmap changes up to last queue id {updates.LastProcessedQueueID}");

                if (updates.BeatmapSetIDs.Any())
                {
                    Console.WriteLine($"Broadcasting new beatmaps to client: {string.Join(',', updates.BeatmapSetIDs.Select(i => i.ToString()))}");
                    await metadataHubContext.Clients.All.SendAsync(nameof(IMetadataClient.BeatmapSetsUpdated), updates);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error during beatmap update polling: {e}");
        }

        timer.Start();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
    }
}
