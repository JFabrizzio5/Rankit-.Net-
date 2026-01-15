using FortniteReplayAPI.Models;
using FortniteReplayReader;
using System.Globalization; // Added for parsing

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

            // Bandera para saber si podemos confiar en los datos oficiales del juego
            bool hasOfficialPlacement = false;

            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (!string.IsNullOrEmpty(p.EpicId))
                    {
                        if (!playersDict.ContainsKey(p.EpicId))
                        {
                            // Intentamos leer el Placement oficial (si existe en tu versión de la librería)
                            // CORRECCIÓN: Manejar el int? (nullable) correctamente
                            int officialRank = p.Placement ?? 0;

                            if (officialRank > 0) hasOfficialPlacement = true;

                            playersDict[p.EpicId] = new MatchResult
                            {
                                Id = p.EpicId,
                                PlayerName = string.IsNullOrEmpty(p.PlayerName) ? p.EpicId : p.PlayerName,
                                IsBot = p.IsBot,
                                TeamId = p.TeamIndex ?? -1,

                                // Si hay dato oficial, lo usamos. Si no, ponemos 999 temporalmente.
                                Rank = officialRank > 0 ? officialRank : 999,

                                Kills = 0,
                                Knocks = 0
                            };
                        }
                    }
                }
            }

            // 3. Procesar Eliminaciones y Derribos (Para contar Kills/Knocks)
            var deathOrder = new List<string>();

            // Diccionario auxiliar para el Fallback (Plan B)
            var lastInteractionTime = new Dictionary<string, float>();
            string? lastKillerId = null;
            float maxDeathTime = -1f;

            if (replay.Eliminations != null)
            {
                foreach (var elim in replay.Eliminations)
                {
                    // CORRECCIÓN: Parsear el tiempo desde string
                    float eventTime = ParseTime(elim.Time);

                    // Registro de tiempos para desempate (solo usado en Plan B)
                    if (!string.IsNullOrEmpty(elim.Eliminator))
                    {
                         if (lastInteractionTime.ContainsKey(elim.Eliminator))
                         {
                             if (eventTime > lastInteractionTime[elim.Eliminator]) lastInteractionTime[elim.Eliminator] = eventTime;
                         }
                         else { lastInteractionTime[elim.Eliminator] = eventTime; }
                    }

                    // === LÓGICA CORREGIDA: DERRIBOS VS KILLS ===

                    // Verificamos si es un Knock usando la propiedad correcta
                    if (elim.Knocked)
                    {
                        if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                        {
                            playersDict[elim.Eliminator].Knocks++;
                        }
                        // Un knock no cuenta como muerte para el Rank, continuamos.
                        continue;
                    }

                    // Si NO es Knock, es una Kill confirmada
                    if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                    {
                        playersDict[elim.Eliminator].Kills++;
                    }

                    if (!string.IsNullOrEmpty(elim.Eliminated))
                    {
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

                        // Datos para Plan B (última muerte)
                        if (eventTime >= maxDeathTime)
                        {
                            maxDeathTime = eventTime;
                            lastKillerId = elim.Eliminator;
                        }
                    }
                }
            }

            // 4. CALCULAR RANKING POR EQUIPOS
            // Si tenemos 'hasOfficialPlacement', CONFÍAMOS EN EL JUEGO y saltamos la lógica manual.

            var teams = playersDict.Values
                .Where(x => x.TeamId != -1)
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            int totalTeams = teams.Count;

            if (hasOfficialPlacement)
            {
                // === ESTRATEGIA A: USAR DATOS OFICIALES ===
                // El rank de un equipo es el MEJOR rank (menor número) de cualquiera de sus miembros.
                foreach (var teamId in teams.Keys)
                {
                    var members = teams[teamId];

                    // Buscamos el mejor rank entre los miembros (ej: uno quedó 5 y el otro 24 -> El equipo es Top 5)
                    // Ignoramos los que tengan 0 o 999 si hay alguno válido.
                    var validRanks = members
                        .Select(pid => playersDict[pid].Rank)
                        .Where(r => r > 0 && r < 999)
                        .ToList();

                    int bestRank = validRanks.Any() ? validRanks.Min() : 999;

                    // Asignamos ese rank a todo el equipo
                    SetTeamRank(playersDict, teamId, bestRank);
                }
            }
            else
            {
                // === ESTRATEGIA B: CÁLCULO MANUAL (FALLBACK) ===
                // Solo se ejecuta si el replay no traía datos de posición.

                var activeTeams = new HashSet<int>(teams.Keys);
                var deadPlayers = new HashSet<string>(deathOrder);
                var wipedTeamsOrder = new List<int>();

                // Detectar equipos eliminados
                foreach (var victimId in deathOrder)
                {
                    if (playersDict.TryGetValue(victimId, out var victim) && victim.TeamId != -1)
                    {
                        int tid = victim.TeamId;
                        if (activeTeams.Contains(tid))
                        {
                            var members = teams[tid];
                            if (members.All(mId => deadPlayers.Contains(mId)))
                            {
                                activeTeams.Remove(tid);
                                wipedTeamsOrder.Add(tid);
                            }
                        }
                    }
                }

                // Asignar Ranks Manualmente
                int currentRank = 1;

                // Ordenar sobrevivientes (Winners + Zombies) por probabilidad de victoria
                var sortedActiveTeams = activeTeams
                    .Select(tid => new
                    {
                        TeamId = tid,
                        CausedLastKill = !string.IsNullOrEmpty(lastKillerId) && teams[tid].Contains(lastKillerId),
                        LastActionTime = teams[tid].Max(pid => lastInteractionTime.ContainsKey(pid) ? lastInteractionTime[pid] : 0f),
                        TeamTotalKills = teams[tid].Sum(pid => playersDict.ContainsKey(pid) ? playersDict[pid].Kills : 0)
                    })
                    .OrderByDescending(t => t.CausedLastKill)
                    .ThenByDescending(t => t.LastActionTime)
                    .ThenByDescending(t => t.TeamTotalKills)
                    .ToList();

                foreach (var activeTeam in sortedActiveTeams)
                {
                    SetTeamRank(playersDict, activeTeam.TeamId, currentRank);
                    currentRank++;
                }

                wipedTeamsOrder.Reverse();
                foreach (var wipedTeamId in wipedTeamsOrder)
                {
                    SetTeamRank(playersDict, wipedTeamId, currentRank);
                    currentRank++;
                }
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
                    // Si usamos dato oficial, Rank ya es correcto.
                    // Si el Rank sigue siendo 999 (error de lectura), le damos 0 puntos.
                    if (player.Rank < 900)
                    {
                        int points = (totalTeams - player.Rank);
                        if (points < 0) points = 0;
                        if (player.Rank == 1) points += rules.WinBonus;
                        rawPlacementPoints = points;
                    }
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
                .ThenByDescending(p => p.Knocks)
                .ToList();

            for (int i = 0; i < playerLeaderboard.Count; i++)
            {
                playerLeaderboard[i].LeaderboardRank = i + 1;
            }

            // B. Leaderboard por Equipos
            var teamLeaderboard = teams.Keys.Select(tid =>
            {
                var members = playersDict.Values.Where(p => p.TeamId == tid).ToList();

                int tRank = members.First().Rank;
                int tPlacementPts = members.First().PlacementPoints;

                int tKills = members.Sum(p => p.Kills);
                int tKnocks = members.Sum(p => p.Knocks);
                int tKillPoints = tKills * rules.PointsPerKill;

                int tTotal = tPlacementPts + tKillPoints;

                return new TeamMatchResult
                {
                    TeamId = tid,
                    Rank = tRank,
                    MemberNames = members.Select(m => m.PlayerName ?? "Unknown").ToList(),
                    TotalKills = tKills,
                    TotalKnocks = tKnocks,
                    PlacementPoints = tPlacementPts,
                    KillPoints = tKillPoints,
                    TotalPoints = tTotal
                };
            })
            .OrderByDescending(t => t.TotalPoints)
            .ThenBy(t => t.Rank)
            .ThenByDescending(t => t.TotalKills)
            .ToList();

            for (int i = 0; i < teamLeaderboard.Count; i++)
            {
                teamLeaderboard[i].LeaderboardRank = i + 1;
            }

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

        // Helper para parsear el tiempo que viene como string
        private float ParseTime(string? timeString)
        {
            if (string.IsNullOrEmpty(timeString)) return 0f;

            // Intentar parsear como float (Invariante para evitar problemas de comas/puntos)
            if (float.TryParse(timeString, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            return 0f;
        }
    }
}