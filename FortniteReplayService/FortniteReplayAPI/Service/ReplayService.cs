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
                                TeamId = p.TeamIndex ?? -1,
                                Rank = 999,
                                Kills = 0,
                                Knocks = 0 // Inicializar
                            };
                        }
                    }
                }
            }

            // 3. Procesar Eliminaciones y Derribos
            var deathOrder = new List<string>(); // Lista de IDs eliminados definitivamente

            if (replay.Eliminations != null)
            {
                foreach (var elim in replay.Eliminations)
                {
                    // === LÓGICA DE DERRIBOS VS KILLS ===

                    // Caso A: Es un derribo (Knock/DBNO)
                    // CORRECCIÓN: Usamos 'Knocked' en lugar de 'IsKnocked'
                    if (elim.Knocked)
                    {
                        // Sumar Knock al atacante
                        if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                        {
                            playersDict[elim.Eliminator].Knocks++;
                        }

                        // IMPORTANTE: Un derribo NO es una muerte definitiva, así que no lo añadimos al deathOrder
                        // y continuamos al siguiente evento.
                        continue;
                    }

                    // Caso B: Es una eliminación confirmada (Kill)

                    // Sumar Kill al eliminador
                    if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                    {
                        playersDict[elim.Eliminator].Kills++;
                    }

                    // Registrar eliminado (solo si es muerte definitiva)
                    if (!string.IsNullOrEmpty(elim.Eliminated))
                    {
                        // Si el eliminado no existe (ej. bots no registrados), lo creamos
                        if (!playersDict.ContainsKey(elim.Eliminated))
                        {
                            playersDict[elim.Eliminated] = new MatchResult
                            {
                                Id = elim.Eliminated,
                                PlayerName = "Unknown",
                                IsBot = true,
                                TeamId = -1,
                                Rank = 999
                            };
                        }
                        deathOrder.Add(elim.Eliminated);
                    }
                }
            }

            // 4. CALCULAR RANKING POR EQUIPOS
            // Un equipo obtiene su rank cuando muere el ÚLTIMO de sus miembros.

            var teams = playersDict.Values
                .Where(x => x.TeamId != -1)
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            var activeTeams = new HashSet<int>(teams.Keys);
            var deadPlayers = new HashSet<string>();
            var wipedTeamsOrder = new List<int>();

            foreach (var victimId in deathOrder)
            {
                deadPlayers.Add(victimId);

                if (playersDict.TryGetValue(victimId, out var victim) && victim.TeamId != -1)
                {
                    int tid = victim.TeamId;

                    if (activeTeams.Contains(tid))
                    {
                        var members = teams[tid];
                        // Verificar si TODOS los miembros están muertos
                        bool isTeamWiped = members.All(mId => deadPlayers.Contains(mId));

                        if (isTeamWiped)
                        {
                            activeTeams.Remove(tid);
                            wipedTeamsOrder.Add(tid);
                        }
                    }
                }
            }

            // 4.1. Asignar Ranks
            int totalTeams = teams.Count;
            int currentRank = 1;

            // Equipos ganadores/vivos
            foreach (var winnerTeamId in activeTeams)
            {
                SetTeamRank(playersDict, winnerTeamId, currentRank);
                currentRank++;
            }

            // Equipos eliminados (orden inverso)
            wipedTeamsOrder.Reverse();

            foreach (var wipedTeamId in wipedTeamsOrder)
            {
                SetTeamRank(playersDict, wipedTeamId, currentRank);
                currentRank++;
            }

            // 5. CALCULAR PUNTOS
            int multiplier = 1;
            if (mode == GameMode.Duos) multiplier = 2;
            if (mode == GameMode.Trios) multiplier = 3;
            if (mode == GameMode.Squads) multiplier = 4;

            foreach (var player in playersDict.Values)
            {
                player.KillPoints = player.Kills * rules.PointsPerKill;

                int rawPlacementPoints = 0;
                if (rules.UseLinearPlacement)
                {
                    int points = (totalTeams - player.Rank);
                    if (points < 0) points = 0;
                    if (player.Rank == 1) points += rules.WinBonus;
                    rawPlacementPoints = points;
                }
                else
                {
                    rawPlacementPoints = CalculateLegacyPoints(player.Rank, rules);
                }

                player.PlacementPoints = rawPlacementPoints * multiplier;
                player.TotalPoints = player.KillPoints + player.PlacementPoints;
            }

            // 6. GENERAR RESPUESTA

            // A. Leaderboard Individual
            var playerLeaderboard = playersDict.Values
                .OrderByDescending(p => p.TotalPoints)
                .ThenByDescending(p => p.Kills)
                .ThenByDescending(p => p.Knocks) // Desempate por knocks
                .ToList();

            // B. Leaderboard por Equipos
            var teamLeaderboard = teams.Keys.Select(tid =>
            {
                var members = playersDict.Values.Where(p => p.TeamId == tid).ToList();

                int tRank = members.First().Rank;
                int tPlacementPts = members.First().PlacementPoints;

                int tKills = members.Sum(p => p.Kills);
                int tKnocks = members.Sum(p => p.Knocks); // <--- SUMA DE KNOCKS DEL EQUIPO
                int tKillPoints = tKills * rules.PointsPerKill;

                int tTotal = tPlacementPts + tKillPoints;

                return new TeamMatchResult
                {
                    TeamId = tid,
                    Rank = tRank,
                    MemberNames = members.Select(m => m.PlayerName ?? "Unknown").ToList(),
                    TotalKills = tKills,
                    TotalKnocks = tKnocks, // <--- ASIGNACIÓN
                    PlacementPoints = tPlacementPts,
                    KillPoints = tKillPoints,
                    TotalPoints = tTotal
                };
            })
            .OrderByDescending(t => t.TotalPoints)
            .ThenBy(t => t.Rank)
            .ThenByDescending(t => t.TotalKills)
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