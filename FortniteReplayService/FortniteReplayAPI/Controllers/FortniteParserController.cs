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
        /// Analiza un torneo completo (múltiples partidas) y calcula acumulados y promedios.
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
            
            // Estructuras temporales para acumular datos
            // Usamos diccionarios para buscar rápido por ID de jugador o ID compuesto de equipo
            var globalTeams = new Dictionary<string, TournamentTeamStats>();
            var globalPlayers = new Dictionary<string, TournamentPlayerStats>();
            var matchSummaries = new List<string>(); // Lista simple de archivos procesados

            foreach (var file in files)
            {
                if (!file.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase)) continue;

                var tempPath = Path.GetTempFileName();
                try 
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create)) await file.CopyToAsync(stream);
                    
                    // Procesar partida individual usando el servicio existente
                    var matchData = _service.ProcessReplay(tempPath, rules, mode);
                    
                    matchSummaries.Add(file.FileName);

                    // 1. ACUMULAR ESTADÍSTICAS INDIVIDUALES (PLAYERS)
                    foreach(var p in matchData.PlayerLeaderboard)
                    {
                        // Si es la primera vez que vemos al jugador, lo inicializamos
                        if(!globalPlayers.ContainsKey(p.Id)) 
                        {
                            globalPlayers[p.Id] = new TournamentPlayerStats 
                            { 
                                Id = p.Id, 
                                Name = p.PlayerName 
                            };
                        }
                        
                        var stat = globalPlayers[p.Id];
                        
                        // Acumuladores básicos
                        stat.MatchesPlayed++;
                        stat.Wins += (p.Rank == 1 ? 1 : 0);
                        stat.TotalKills += p.Kills;
                        
                        // Acumuladores de Puntos
                        stat.KillPoints += p.KillPoints;
                        stat.PlacementPoints += p.PlacementPoints;
                        stat.TotalPoints += p.TotalPoints;

                        // Acumulador para promedios
                        stat.SumRank += p.Rank;
                    }

                    // 2. ACUMULAR ESTADÍSTICAS DE EQUIPO (TEAMS)
                    foreach(var t in matchData.TeamLeaderboard)
                    {
                        // Como el TeamId (int) cambia en cada partida, usamos los nombres de los miembros como clave única.
                        // Ordenamos los nombres para asegurar consistencia (A, B es igual que B, A).
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
                        
                        // Acumuladores básicos
                        tStat.MatchesPlayed++;
                        tStat.Wins += (t.Rank == 1 ? 1 : 0);
                        tStat.TotalKills += t.TotalKills;

                        // Acumuladores de Puntos
                        tStat.KillPoints += t.KillPoints;
                        tStat.PlacementPoints += t.PlacementPoints;
                        tStat.TotalPoints += t.TotalPoints;

                        // Acumulador para promedios
                        tStat.SumRank += t.Rank;
                    }
                }
                catch(Exception ex)
                {
                    // Registramos el error pero continuamos con el siguiente archivo
                    matchSummaries.Add($"{file.FileName} (ERROR: {ex.Message})");
                }
                finally 
                { 
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); 
                }
            }

            // 3. GENERAR RESULTADOS FINALES CON PROMEDIOS
            
            // Equipos
            var finalTeamLeaderboard = globalTeams.Values
                .Select(t => new TournamentTeamResult
                {
                    // Asignamos un ID ficticio o basado en hash si se requiere, aquí lo dejamos simple
                    TeamId = t.GetHashCode(), 
                    MemberNames = t.MemberNames,
                    
                    MatchesPlayed = t.MatchesPlayed,
                    Wins = t.Wins,
                    TotalKills = t.TotalKills,
                    
                    KillPoints = t.KillPoints,
                    PlacementPoints = t.PlacementPoints,
                    TotalPoints = t.TotalPoints,
                    
                    // Cálculos de Promedio (evitando división por cero)
                    AverageRank = t.MatchesPlayed > 0 
                        ? Math.Round((double)t.SumRank / t.MatchesPlayed, 2) 
                        : 0,
                    AverageKills = t.MatchesPlayed > 0 
                        ? Math.Round((double)t.TotalKills / t.MatchesPlayed, 2) 
                        : 0
                })
                .OrderByDescending(x => x.TotalPoints)
                .ThenByDescending(x => x.Wins)
                .ThenByDescending(x => x.TotalKills)
                .ThenBy(x => x.AverageRank) // Menor rank promedio es mejor desempate
                .ToList();

            // Jugadores
            var finalPlayerLeaderboard = globalPlayers.Values
                .Select(p => new TournamentPlayerResult
                {
                    Id = p.Id,
                    PlayerName = p.Name,
                    
                    MatchesPlayed = p.MatchesPlayed,
                    Wins = p.Wins,
                    TotalKills = p.TotalKills,
                    
                    KillPoints = p.KillPoints,
                    PlacementPoints = p.PlacementPoints,
                    TotalPoints = p.TotalPoints,
                    
                    // Cálculos de Promedio
                    AverageRank = p.MatchesPlayed > 0 
                        ? Math.Round((double)p.SumRank / p.MatchesPlayed, 2) 
                        : 0,
                    AverageKills = p.MatchesPlayed > 0 
                        ? Math.Round((double)p.TotalKills / p.MatchesPlayed, 2) 
                        : 0
                })
                .OrderByDescending(x => x.TotalPoints)
                .ThenByDescending(x => x.Wins)
                .ThenByDescending(x => x.TotalKills)
                .ToList();

            return Ok(new TournamentAnalysisResponse
            {
                TotalMatches = files.Count,
                ProcessedFiles = matchSummaries,
                OverallTeamLeaderboard = finalTeamLeaderboard,
                OverallPlayerLeaderboard = finalPlayerLeaderboard,
                MatchDetails = new List<MatchAnalysisResponse>() // Se puede llenar si se desea detalle completo
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
            int winBonus = 5; 
            if (mode == GameMode.Trios) winBonus = 15; 

            return new ScoringRules
            {
                UseLinearPlacement = true,
                PointsPerKill = 2,
                WinBonus = winBonus
            };
        }

        // --- DTOs INTERNOS PARA ACUMULACIÓN (Privados) ---
        // Estas clases se usan solo dentro del controlador para ir sumando valores mientras se leen los archivos.

        private class TournamentPlayerStats 
        { 
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            
            public int MatchesPlayed { get; set; }
            public int Wins { get; set; }
            public int TotalKills { get; set; }
            
            // Acumuladores de Puntos
            public int TotalPoints { get; set; } 
            public int KillPoints { get; set; }
            public int PlacementPoints { get; set; }
            
            // Para calcular promedios
            public int SumRank { get; set; } 
        }

        private class TournamentTeamStats 
        { 
            public List<string> MemberNames { get; set; } = new List<string>();
            
            public int MatchesPlayed { get; set; }
            public int Wins { get; set; }
            public int TotalKills { get; set; }
            
            // Acumuladores de Puntos
            public int TotalPoints { get; set; } 
            public int KillPoints { get; set; }
            public int PlacementPoints { get; set; }
            
            // Para calcular promedios
            public int SumRank { get; set; } 
        }
    }
}