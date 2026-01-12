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

        /// <summary>
        /// Analiza una sola partida.
        /// </summary>
        [HttpPost("analyze")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Analyze(
            [Required] IFormFile file,
            [FromForm] GameMode mode = GameMode.Solos, // 1=Solos, 2=Duos, 3=Trios, 4=Squads
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

        /// <summary>
        /// Analiza un torneo completo (múltiples partidas).
        /// </summary>
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
            
            // Estructuras para acumular datos
            var globalTeams = new Dictionary<string, TournamentTeamStats>();
            var globalPlayers = new Dictionary<string, TournamentPlayerStats>();
            var matchSummaries = new List<object>();

            foreach (var file in files)
            {
                if (!file.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase)) continue;

                var tempPath = Path.GetTempFileName();
                try 
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create)) await file.CopyToAsync(stream);
                    
                    // Procesar partida individual
                    var matchData = _service.ProcessReplay(tempPath, rules, mode);
                    
                    matchSummaries.Add(new 
                    { 
                        File = file.FileName, 
                        Teams = matchData.TotalTeams, 
                        Players = matchData.TotalPlayers 
                    });

                    // 1. Acumular Individuales
                    foreach(var p in matchData.PlayerLeaderboard)
                    {
                        // Usar EpicId como clave
                        if(!globalPlayers.ContainsKey(p.Id)) 
                            globalPlayers[p.Id] = new TournamentPlayerStats { Name = p.PlayerName };
                        
                        var stat = globalPlayers[p.Id];
                        stat.TotalKills += p.Kills;
                        stat.TotalPoints += p.TotalPoints;
                        stat.MatchesPlayed++;
                        stat.Wins += (p.Rank == 1 ? 1 : 0);
                    }

                    // 2. Acumular Equipos
                    // Como el TeamId cambia entre partidas, usamos los NOMBRES de los miembros como clave única.
                    foreach(var t in matchData.TeamLeaderboard)
                    {
                        // Normalizamos la clave: Nombres ordenados alfabéticamente y unidos
                        t.MemberNames.Sort();
                        string teamKey = string.Join(" | ", t.MemberNames);

                        if(!globalTeams.ContainsKey(teamKey)) 
                            globalTeams[teamKey] = new TournamentTeamStats { MemberNames = t.MemberNames };

                        var tStat = globalTeams[teamKey];
                        tStat.TotalPoints += t.TotalPoints;
                        tStat.TotalKills += t.TotalKills;
                        tStat.MatchesPlayed++;
                        tStat.Wins += (t.Rank == 1 ? 1 : 0);
                    }
                }
                catch(Exception ex)
                {
                    matchSummaries.Add(new { File = file.FileName, Error = ex.Message });
                }
                finally 
                { 
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); 
                }
            }

            return Ok(new 
            {
                TournamentMode = mode,
                TotalMatches = files.Count,
                MatchesDetails = matchSummaries,
                // Ordenar globales por Puntos > Wins > Kills
                GlobalTeamLeaderboard = globalTeams.Values
                    .OrderByDescending(x => x.TotalPoints)
                    .ThenByDescending(x => x.Wins)
                    .ThenByDescending(x => x.TotalKills)
                    .ToList(),
                GlobalPlayerLeaderboard = globalPlayers.Values
                    .OrderByDescending(x => x.TotalPoints)
                    .ThenByDescending(x => x.Wins)
                    .ThenByDescending(x => x.TotalKills)
                    .ToList()
            });
        }

        // --- Helpers ---

        private ScoringRules GetRules(string? json, GameMode mode)
        {
            // Intentar parsear JSON del cliente
            if (!string.IsNullOrWhiteSpace(json))
            {
                try 
                { 
                    return JsonSerializer.Deserialize<ScoringRules>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; 
                }
                catch 
                { 
                    // Si falla, ignoramos y usamos defaults
                }
            }

            // --- REGLAS POR DEFECTO ---
            // Basadas en la solicitud del usuario
            int winBonus = 5; // Default (Solos/Duos/Squads)
            if (mode == GameMode.Trios) winBonus = 15; // Trios específicamente pidió 15 pts por victoria

            return new ScoringRules
            {
                UseLinearPlacement = true,
                PointsPerKill = 2,
                WinBonus = winBonus
            };
        }

        // DTOs para el reporte de torneo
        public class TournamentPlayerStats 
        { 
            public string Name { get; set; } = "";
            public int TotalPoints { get; set; } 
            public int TotalKills { get; set; } 
            public int Wins { get; set; }
            public int MatchesPlayed { get; set; }
        }

        public class TournamentTeamStats 
        { 
            public List<string> MemberNames { get; set; } = new List<string>();
            public int TotalPoints { get; set; } 
            public int TotalKills { get; set; } 
            public int Wins { get; set; }
            public int MatchesPlayed { get; set; }
        }
    }
}