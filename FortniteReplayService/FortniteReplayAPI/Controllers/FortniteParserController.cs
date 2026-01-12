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
        /// Sube un archivo .replay y calcula la tabla de posiciones según las reglas JSON enviadas.
        /// </summary>
        /// <param name="file">El archivo .replay de Fortnite.</param>
        /// <param name="rulesJson">
        /// Cadena JSON con las reglas. 
        /// Ejemplo: { "pointsPerKill": 2, "thresholds": [{"thresholdRank": 1, "points": 10}], "ranges": [{"startRank": 50, "endRank": 1, "pointsPerStep": 1}] }
        /// </param>
        /// <returns>Lista de resultados ordenados.</returns>
        [HttpPost("analyze")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Analyze(
            [Required] IFormFile file,
            [FromForm] string? rulesJson)
        {
            // Validaciones iniciales
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No se ha subido ningún archivo." });
            }

            if (!Path.GetExtension(file.FileName).Equals(".replay", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "El archivo debe tener extensión .replay" });
            }

            // Parsear reglas
            ScoringRules rules = ParseRules(rulesJson);
            if (rules == null) return BadRequest(new { error = "El formato JSON de 'rulesJson' es inválido." });

            // Procesar un solo archivo
            try
            {
                var result = await ProcessSingleReplay(file, rules);
                return Ok(new
                {
                    FileName = file.FileName,
                    ProcessedAt = DateTime.UtcNow,
                    TotalPlayers = result.Count,
                    Leaderboard = result.Take(100)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error interno: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sube MÚLTIPLES archivos .replay y genera una Tabla Global acumulada (Torneo).
        /// </summary>
        /// <param name="files">Lista de archivos .replay.</param>
        /// <param name="rulesJson">Reglas de puntuación aplicables a TODAS las partidas.</param>
        /// <returns>Ranking global acumulado.</returns>
        [HttpPost("analyze-tournament")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalyzeTournament(
            [Required] List<IFormFile> files,
            [FromForm] string? rulesJson)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { error = "No se han subido archivos." });

            // 1. Parsear reglas una sola vez para todas las partidas
            ScoringRules rules = ParseRules(rulesJson);
            if (rules == null) return BadRequest(new { error = "JSON de reglas inválido." });

            var globalStats = new Dictionary<string, TournamentPlayerStats>();
            var matchSummaries = new List<object>();

            // 2. Iterar sobre cada archivo subido
            foreach (var file in files)
            {
                if (!Path.GetExtension(file.FileName).Equals(".replay", StringComparison.OrdinalIgnoreCase))
                    continue; // Saltar archivos que no sean replay

                try
                {
                    // Procesar partida individual
                    var matchResults = await ProcessSingleReplay(file, rules);

                    // Guardar resumen breve de esta partida
                    matchSummaries.Add(new 
                    { 
                        FileName = file.FileName, 
                        PlayerCount = matchResults.Count 
                    });

                    // 3. ACUMULAR PUNTOS (Lógica de Torneo)
                    foreach (var player in matchResults)
                    {
                        // Usamos el ID (EpicID) como clave única. Si es bot o desconocido, usamos el nombre.
                        string key = !string.IsNullOrEmpty(player.Id) ? player.Id : player.PlayerName;

                        if (!globalStats.ContainsKey(key))
                        {
                            globalStats[key] = new TournamentPlayerStats
                            {
                                PlayerName = player.PlayerName,
                                IsBot = player.IsBot
                            };
                        }

                        var stats = globalStats[key];
                        stats.TotalKills += player.Kills;
                        stats.TotalPoints += player.TotalPoints;
                        stats.MatchesPlayed++;
                        stats.Placements.Add(player.Rank);
                    }
                }
                catch (Exception ex)
                {
                    // Si falla una replay, la registramos pero seguimos con las demás
                    matchSummaries.Add(new { FileName = file.FileName, Error = ex.Message });
                }
            }

            // 4. Ordenar Tabla Global
            var leaderboard = globalStats.Values
                .OrderByDescending(p => p.TotalPoints)
                .ThenByDescending(p => p.TotalKills)
                .ThenBy(p => p.AveragePlacement)
                .ToList();

            return Ok(new
            {
                TournamentProcessedAt = DateTime.UtcNow,
                TotalMatches = files.Count,
                MatchesDetails = matchSummaries,
                GlobalLeaderboard = leaderboard
            });
        }

        // --- Helpers Privados ---

        private ScoringRules ParseRules(string? json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return new ScoringRules();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<ScoringRules>(json, options) ?? new ScoringRules();
            }
            catch
            {
                return null!;
            }
        }

        private async Task<List<MatchResult>> ProcessSingleReplay(IFormFile file, ScoringRules rules)
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                return _service.ProcessReplay(tempPath, rules);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
        }

        // Clase interna para llevar el conteo del torneo
        private class TournamentPlayerStats
        {
            public string PlayerName { get; set; } = "";
            public bool IsBot { get; set; }
            public int TotalPoints { get; set; }
            public int TotalKills { get; set; }
            public int MatchesPlayed { get; set; }
            public List<int> Placements { get; set; } = new List<int>();
            
            public double AveragePlacement => Placements.Count > 0 ? Math.Round(Placements.Average(), 1) : 0;
        }
    }
}