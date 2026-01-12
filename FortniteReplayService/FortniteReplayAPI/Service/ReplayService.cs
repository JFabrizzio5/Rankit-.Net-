using FortniteReplayAPI.Models;
using FortniteReplayReader;

namespace FortniteReplayAPI.Services
{
    public class ReplayService
    {
        private readonly ILogger<ReplayReader> _logger;

        public ReplayService(ILogger<ReplayReader> logger)
        {
            _logger = logger;
        }

        public MatchAnalysisResponse ProcessReplay(string filePath, ScoringRules rules, GameMode mode)
        {
            // 1. Leer el archivo de replay
            var reader = new ReplayReader(_logger);
            var replay = reader.ReadReplay(filePath);

            if (replay == null) throw new Exception("No se pudo leer el archivo .replay (formato inválido o corrupto).");

            // 2. Mapear Jugadores y Equipos
            // Usamos un diccionario para acceso rápido por EpicId
            var playersDict = new Dictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);
            
            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (!string.IsNullOrEmpty(p.EpicId))
                    {
                        if (!playersDict.ContainsKey(p.EpicId))
                        {
                            playersDict[p.EpicId] = new MatchResult
                            {
                                Id = p.EpicId,
                                PlayerName = string.IsNullOrEmpty(p.PlayerName) ? p.EpicId : p.PlayerName,
                                IsBot = p.IsBot,
                                TeamId = p.TeamIndex ?? -1, // Fix: Handle nullable int with fallback
                                Rank = 999 // Valor temporal
                            };
                        }
                    }
                }
            }

            // 3. Procesar Muertes y determinar orden
            var deathOrder = new List<string>(); // Lista de IDs eliminados en orden cronológico

            if (replay.Eliminations != null)
            {
                foreach (var elim in replay.Eliminations)
                {
                    // Asignar Kill al eliminador
                    if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                    {
                        playersDict[elim.Eliminator].Kills++;
                    }

                    // Registrar eliminado
                    if (!string.IsNullOrEmpty(elim.Eliminated))
                    {
                        // Si el eliminado no existe (ej. bots no registrados en PlayerData), lo creamos
                        if (!playersDict.ContainsKey(elim.Eliminated))
                        {
                            playersDict[elim.Eliminated] = new MatchResult 
                            { 
                                Id = elim.Eliminated, 
                                PlayerName = "Unknown", 
                                IsBot = true, 
                                TeamId = -1, // Equipo desconocido
                                Rank = 999 
                            };
                        }
                        deathOrder.Add(elim.Eliminated);
                    }
                }
            }

            // 4. CALCULAR RANKING POR EQUIPOS
            // Un equipo obtiene su rank cuando muere el ÚLTIMO de sus miembros.
            
            // Agrupar IDs por TeamId (ignorando el equipo -1 desconocido)
            var teams = playersDict.Values
                .Where(x => x.TeamId != -1)
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            var activeTeams = new HashSet<int>(teams.Keys);
            var deadPlayers = new HashSet<string>();
            var wipedTeamsOrder = new List<int>(); // Equipos ordenados por eliminación

            // Recorremos las muertes cronológicamente para ver cuándo cae un equipo completo
            foreach (var victimId in deathOrder)
            {
                deadPlayers.Add(victimId);

                if (playersDict.TryGetValue(victimId, out var victim) && victim.TeamId != -1)
                {
                    int tid = victim.TeamId;
                    
                    // Si el equipo aún está "vivo" en nuestro registro
                    if (activeTeams.Contains(tid))
                    {
                        var members = teams[tid];
                        // Verificar si TODOS los miembros están muertos
                        bool isTeamWiped = members.All(mId => deadPlayers.Contains(mId));

                        if (isTeamWiped)
                        {
                            activeTeams.Remove(tid);
                            wipedTeamsOrder.Add(tid); // Este equipo acaba de ser eliminado completamente
                        }
                    }
                }
            }

            // 4.1. Asignar Ranks
            int totalTeams = teams.Count;
            int currentRank = 1;

            // Los equipos que quedaron en activeTeams son los ganadores (Top 1, o Top 1, 2, 3 si cortó el replay)
            // Normalmente solo queda 1 equipo vivo (el ganador).
            foreach (var winnerTeamId in activeTeams)
            {
                SetTeamRank(playersDict, winnerTeamId, currentRank);
                // Si hubiera múltiples vivos (bug o replay cortado), incrementamos el rank
                // Para simplificar, asumiremos que comparten Top 1 o siguen secuencia.
                // Aquí seguiremos secuencia para evitar duplicados masivos.
                currentRank++; 
            }

            // Los equipos eliminados se rankean de atrás hacia adelante
            // El último en wipedTeamsOrder fue el último en morir (mejor rank después de los vivos)
            wipedTeamsOrder.Reverse(); 

            foreach (var wipedTeamId in wipedTeamsOrder)
            {
                SetTeamRank(playersDict, wipedTeamId, currentRank);
                currentRank++;
            }

            // 5. CALCULAR PUNTOS
            // Multiplicador según modo: Solos=1, Duos=2, Trios=3, Squads=4 (o custom)
            // Según tu petición: Duos=2, Trios=3.
            int multiplier = 1;
            if (mode == GameMode.Duos) multiplier = 2;
            if (mode == GameMode.Trios) multiplier = 3;
            if (mode == GameMode.Squads) multiplier = 4; // Asumimos 4 para squads

            foreach (var player in playersDict.Values)
            {
                // A. Puntos por Kill (Siempre igual por defecto, o configurable)
                player.KillPoints = player.Kills * rules.PointsPerKill;

                // B. Puntos por Placement (Basado en el Rank del Equipo)
                int rawPlacementPoints = 0;

                if (rules.UseLinearPlacement)
                {
                    // Lógica: "Si se metieron 25, el primero en morir (rank 25) no puntua nada"
                    // Rank 25 -> 25 - 25 = 0.
                    // Rank 1 -> 25 - 1 = 24.
                    int points = (totalTeams - player.Rank);
                    if (points < 0) points = 0;

                    // Bonus por Victoria (Top 1)
                    if (player.Rank == 1)
                    {
                        // Prioridad al bonus específico, si no usa el general
                        points += rules.WinBonus;
                    }

                    rawPlacementPoints = points;
                }
                else
                {
                    // Lógica antigua (Thresholds)
                    rawPlacementPoints = CalculateLegacyPoints(player.Rank, rules);
                }

                // Aplicar Multiplicador de Modalidad al Placement
                player.PlacementPoints = rawPlacementPoints * multiplier;

                // Total Individual
                player.TotalPoints = player.KillPoints + player.PlacementPoints;
            }

            // 6. GENERAR RESPUESTA

            // A. Leaderboard Individual
            var playerLeaderboard = playersDict.Values
                .OrderByDescending(p => p.TotalPoints)
                .ThenByDescending(p => p.Kills)
                .ToList();

            // B. Leaderboard por Equipos
            var teamLeaderboard = teams.Keys.Select(tid => 
            {
                var members = playersDict.Values.Where(p => p.TeamId == tid).ToList();
                
                // Datos compartidos del equipo
                int tRank = members.First().Rank;
                int tPlacementPts = members.First().PlacementPoints; // Ya tiene el multiplicador
                
                // Datos acumulados
                int tKills = members.Sum(p => p.Kills);
                int tKillPoints = tKills * rules.PointsPerKill;
                
                // Total del Equipo = Puntos de Posición + Puntos de Kill de todos
                int tTotal = tPlacementPts + tKillPoints;

                return new TeamMatchResult
                {
                    TeamId = tid,
                    Rank = tRank,
                    MemberNames = members.Select(m => m.PlayerName ?? "Unknown").ToList(),
                    TotalKills = tKills,
                    PlacementPoints = tPlacementPts,
                    KillPoints = tKillPoints,
                    TotalPoints = tTotal
                };
            })
            .OrderByDescending(t => t.TotalPoints) // Primero puntos totales
            .ThenBy(t => t.Rank)                   // Desempate por posición
            .ThenByDescending(t => t.TotalKills)   // Desempate por kills
            .ToList();

            return new MatchAnalysisResponse
            {
                FileName = Path.GetFileName(filePath),
                ProcessedAt = DateTime.UtcNow,
                TotalTeams = totalTeams,
                TotalPlayers = playersDict.Count,
                Mode = mode,
                TeamLeaderboard = teamLeaderboard,
                PlayerLeaderboard = playerLeaderboard
            };
        }

        // Helper para asignar rank a todos los miembros de un equipo
        private void SetTeamRank(Dictionary<string, MatchResult> dict, int teamId, int rank)
        {
            foreach (var p in dict.Values.Where(x => x.TeamId == teamId))
            {
                p.Rank = rank;
            }
        }

        private int CalculateLegacyPoints(int rank, ScoringRules rules)
        {
            int pts = 0;
            if (rules.Thresholds != null)
            {
                foreach (var t in rules.Thresholds)
                {
                    if (rank <= t.ThresholdRank) pts += t.Points;
                }
            }
            if (rules.Ranges != null)
            {
                foreach (var r in rules.Ranges)
                {
                    if (rank > r.StartRank) continue;
                    int effectiveEnd = Math.Max(rank, r.EndRank);
                    int steps = (r.StartRank - effectiveEnd) + 1;
                    if (steps > 0) pts += (steps * r.PointsPerStep);
                }
            }
            return pts;
        }
    }
}