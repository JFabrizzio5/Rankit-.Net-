using FortniteReplayAPI.Models;
using FortniteReplayAPI.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FortniteReplayAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FortniteParserController : ControllerBase
    {
        private readonly ReplayService _service;

        public FortniteParserController(ReplayService service)
        {
            _service = service;
        }

        // --- ENDPOINT MODIFICADO: Usa el modelo independiente ---
        [HttpPost("analyze-summary")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalyzeSummary([Required] IFormFile file)
        {
            if (file == null || !file.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Archivo inválido. Debe ser .replay" });

            var tempPath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Llamada al método separado que devuelve ReplaySummaryResponse
                var summaryResult = _service.GetReplaySummary(tempPath);

                return Ok(summaryResult);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error obteniendo resumen: {ex.Message}" });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }

        [HttpPost("analyze")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Analyze(
            [Required] IFormFile file,
            [FromForm] GameMode mode = GameMode.Solos,
            [FromForm] string? rulesJson = null)
        {
            if (file == null || !file.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Archivo inválido. Debe ser .replay" });

            ScoringRules rules = GetRules(rulesJson, mode);

            var tempPath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var result = _service.ProcessReplay(tempPath, rules, mode);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error procesando replay: {ex.Message}" });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }

        [HttpPost("analyze-tournament")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalyzeTournament(
            [Required] List<IFormFile> files,
            [FromForm] GameMode mode = GameMode.Solos,
            [FromForm] string? rulesJson = null)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { error = "No se han subido archivos." });

            var rules = GetRules(rulesJson, mode);

            var globalTeams = new Dictionary<string, TournamentTeamStats>();
            var globalPlayers = new Dictionary<string, TournamentPlayerStats>();
            var matchSummaries = new List<string>();

            foreach (var file in files)
            {
                if (!file.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase)) continue;

                var tempPath = Path.GetTempFileName();
                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create)) await file.CopyToAsync(stream);

                    var matchData = _service.ProcessReplay(tempPath, rules, mode);
                    matchSummaries.Add(file.FileName);

                    // 1. ACUMULAR ESTADÍSTICAS INDIVIDUALES
                    foreach(var p in matchData.PlayerLeaderboard)
                    {
                        if(!globalPlayers.ContainsKey(p.Id))
                        {
                            globalPlayers[p.Id] = new TournamentPlayerStats
                            {
                                Id = p.Id,
                                Name = p.PlayerName
                            };
                        }

                        var stat = globalPlayers[p.Id];

                        stat.MatchesPlayed++;
                        stat.Wins += (p.Rank == 1 ? 1 : 0);
                        stat.TotalKills += p.Kills;
                        stat.TotalKnocks += p.Knocks;

                        stat.KillPoints += p.KillPoints;
                        stat.PlacementPoints += p.PlacementPoints;
                        stat.TotalPoints += p.TotalPoints;

                        stat.SumRank += p.Rank;
                    }

                    // 2. ACUMULAR ESTADÍSTICAS DE EQUIPO
                    foreach(var t in matchData.TeamLeaderboard)
                    {
                        t.MemberNames.Sort();
                        string teamKey = string.Join(" | ", t.MemberNames);

                        if(!globalTeams.ContainsKey(teamKey))
                        {
                            globalTeams[teamKey] = new TournamentTeamStats
                            {
                                MemberNames = t.MemberNames
                            };
                        }

                        var tStat = globalTeams[teamKey];

                        tStat.MatchesPlayed++;
                        tStat.Wins += (t.Rank == 1 ? 1 : 0);
                        tStat.TotalKills += t.TotalKills;
                        tStat.TotalKnocks += t.TotalKnocks;

                        tStat.KillPoints += t.KillPoints;
                        tStat.PlacementPoints += t.PlacementPoints;
                        tStat.TotalPoints += t.TotalPoints;

                        tStat.SumRank += t.Rank;
                    }
                }
                catch(Exception ex)
                {
                    matchSummaries.Add($"{file.FileName} (ERROR: {ex.Message})");
                }
                finally
                {
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
                }
            }

            // 3. GENERAR RESULTADOS FINALES

            var finalTeamLeaderboard = globalTeams.Values
                .Select(t => new TournamentTeamResult
                {
                    TeamId = t.GetHashCode(),
                    MemberNames = t.MemberNames,
                    MatchesPlayed = t.MatchesPlayed,
                    Wins = t.Wins,
                    TotalKills = t.TotalKills,
                    TotalKnocks = t.TotalKnocks,
                    KillPoints = t.KillPoints,
                    PlacementPoints = t.PlacementPoints,
                    TotalPoints = t.TotalPoints,
                    AverageRank = t.MatchesPlayed > 0 ? Math.Round((double)t.SumRank / t.MatchesPlayed, 2) : 0,
                    AverageKills = t.MatchesPlayed > 0 ? Math.Round((double)t.TotalKills / t.MatchesPlayed, 2) : 0,
                    AverageKnocks = t.MatchesPlayed > 0 ? Math.Round((double)t.TotalKnocks / t.MatchesPlayed, 2) : 0
                })
                .OrderByDescending(x => x.TotalPoints)
                .ThenByDescending(x => x.Wins)
                .ThenByDescending(x => x.TotalKills)
                .ThenByDescending(x => x.TotalKnocks)
                .ToList();

            var finalPlayerLeaderboard = globalPlayers.Values
                .Select(p => new TournamentPlayerResult
                {
                    Id = p.Id,
                    PlayerName = p.Name,
                    MatchesPlayed = p.MatchesPlayed,
                    Wins = p.Wins,
                    TotalKills = p.TotalKills,
                    TotalKnocks = p.TotalKnocks,
                    KillPoints = p.KillPoints,
                    PlacementPoints = p.PlacementPoints,
                    TotalPoints = p.TotalPoints,
                    AverageRank = p.MatchesPlayed > 0 ? Math.Round((double)p.SumRank / p.MatchesPlayed, 2) : 0,
                    AverageKills = p.MatchesPlayed > 0 ? Math.Round((double)p.TotalKills / p.MatchesPlayed, 2) : 0,
                    AverageKnocks = p.MatchesPlayed > 0 ? Math.Round((double)p.TotalKnocks / p.MatchesPlayed, 2) : 0
                })
                .OrderByDescending(x => x.TotalPoints)
                .ThenByDescending(x => x.Wins)
                .ThenByDescending(x => x.TotalKills)
                .ThenByDescending(x => x.TotalKnocks)
                .ToList();

            return Ok(new TournamentAnalysisResponse
            {
                TotalMatches = files.Count,
                ProcessedFiles = matchSummaries,
                OverallTeamLeaderboard = finalTeamLeaderboard,
                OverallPlayerLeaderboard = finalPlayerLeaderboard,
                MatchDetails = new List<MatchAnalysisResponse>()
            });
        }

        private ScoringRules GetRules(string? json, GameMode mode)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    return JsonSerializer.Deserialize<ScoringRules>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                }
                catch {}
            }

            int winBonus = 5;
            if (mode == GameMode.Trios) winBonus = 15;

            return new ScoringRules
            {
                UseLinearPlacement = true,
                PointsPerKill = 2,
                WinBonus = winBonus
            };
        }

        private class TournamentPlayerStats
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public int MatchesPlayed { get; set; }
            public int Wins { get; set; }
            public int TotalKills { get; set; }
            public int TotalKnocks { get; set; }
            public int TotalPoints { get; set; }
            public int KillPoints { get; set; }
            public int PlacementPoints { get; set; }
            public int SumRank { get; set; }
        }

        private class TournamentTeamStats
        {
            public List<string> MemberNames { get; set; } = new List<string>();
            public int MatchesPlayed { get; set; }
            public int Wins { get; set; }
            public int TotalKills { get; set; }
            public int TotalKnocks { get; set; }
            public int TotalPoints { get; set; }
            public int KillPoints { get; set; }
            public int PlacementPoints { get; set; }
            public int SumRank { get; set; } 
        }
    }
}