// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Online.Rooms;

namespace osu.Server.Spectator.Controllers
{
    public class SoloController : Controller
    {
        [HttpPost("solo/beatmap/{beatmapId}/scores")]
        public APIScoreToken RequestToken(int beatmapId)
        {
            if (!Request.Form.TryGetValue("version_hash", out var versionHash))
            {
                HttpContext.Response.StatusCode = 422;
                throw new ArgumentException("Missing version hash");
            }

            // todo: verify version hash against database.
            if (string.IsNullOrEmpty(versionHash))
                throw new ArgumentException("Empty version hash");

            return new APIScoreToken { ID = 123 };
        }

        [HttpPut("solo/beatmap/{beatmapId}/scores/{scoreId}")]
        public MultiplayerScore SubmitScore(int beatmapId, long scoreId)
        {
            return new MultiplayerScore
            {
                ID = (int)scoreId,
            };
        }
    }
}
