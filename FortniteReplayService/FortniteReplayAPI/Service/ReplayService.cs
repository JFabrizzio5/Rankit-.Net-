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

        public List<MatchResult> ProcessReplay(string filePath, ScoringRules rules)
        {
            // 1. Leer el archivo con la librería
            var reader = new ReplayReader(_logger);
            var replay = reader.ReadReplay(filePath);

            if (replay == null) throw new Exception("No se pudo leer el archivo .replay (formato inválido o corrupto).");

            // 2. Diccionario para rastrear jugadores
            var playersDict = new Dictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);

            // Cargar datos básicos de jugadores
            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (!string.IsNullOrEmpty(p.EpicId))
                    {
                        // Si ya existe (a veces duplicado), actualizamos nombre, si no, creamos
                        if (!playersDict.ContainsKey(p.EpicId))
                        {
                            playersDict[p.EpicId] = new MatchResult
                            {
                                Id = p.EpicId,
                                PlayerName = string.IsNullOrEmpty(p.PlayerName) ? p.EpicId : p.PlayerName,
                                IsBot = p.IsBot,
                                Rank = 999 // Temporal
                            };
                        }
                        else if (!string.IsNullOrEmpty(p.PlayerName)) // Actualizar nombre si es mejor
                        {
                            playersDict[p.EpicId].PlayerName = p.PlayerName;
                        }
                    }
                }
            }

            // 3. Procesar Kills y Orden de Muerte
            var deathOrder = new List<string>(); // Lista ordenada cronológicamente de eliminados

            if (replay.Eliminations != null)
            {
                foreach (var elim in replay.Eliminations)
                {
                    // Sumar Kill al eliminador
                    if (!string.IsNullOrEmpty(elim.Eliminator) && playersDict.ContainsKey(elim.Eliminator))
                    {
                        playersDict[elim.Eliminator].Kills++;
                    }

                    // Registrar eliminado para el Top
                    if (!string.IsNullOrEmpty(elim.Eliminated))
                    {
                        // Asegurar que el eliminado exista en el dict (a veces faltan en PlayerData)
                        if (!playersDict.ContainsKey(elim.Eliminated))
                        {
                            playersDict[elim.Eliminated] = new MatchResult 
                            { 
                                Id = elim.Eliminated, 
                                PlayerName = "Unknown/Bot", 
                                IsBot = true,
                                Rank = 999 
                            };
                        }
                        
                        deathOrder.Add(elim.Eliminated);
                    }
                }
            }

            // 4. Calcular Rankings (Posiciones)
            // Filtramos muertes duplicadas (el último evento de muerte es el real)
            var uniqueDeaths = deathOrder
                .GroupBy(id => id)
                .Select(g => g.Last())
                .ToList();

            // Los sobrevivientes son los que están en el diccionario pero NO en la lista de muertes
            var allIds = playersDict.Keys.ToList();
            var survivors = allIds.Where(id => !uniqueDeaths.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();

            int currentRank = 1;

            // A. Asignar Rank 1 a los sobrevivientes (pueden ser varios si el replay corta antes)
            foreach (var survId in survivors)
            {
                playersDict[survId].Rank = 1;
            }

            // Si hay 1 ganador, el siguiente puesto es el 2. Si hay 2 vivos, el siguiente es el 3.
            int nextRank = (survivors.Count > 0) ? survivors.Count + 1 : 1;

            // B. Asignar Rank a los eliminados (Invertimos la lista: último en morir = mejor puesto)
            uniqueDeaths.Reverse();

            foreach (var victimId in uniqueDeaths)
            {
                if (playersDict.ContainsKey(victimId))
                {
                    playersDict[victimId].Rank = nextRank;
                }
                nextRank++;
            }

            // 5. CALCULAR PUNTOS (Usando las reglas dinámicas)
            var results = playersDict.Values.ToList();
            foreach (var player in results)
            {
                CalculatePlayerPoints(player, rules);
            }

            // 6. Retornar ordenado
            return results
                .OrderByDescending(p => p.TotalPoints) // Primero por puntos
                .ThenBy(p => p.Rank)                   // Luego por Top (desempate)
                .ThenByDescending(p => p.Kills)        // Luego por Kills
                .ToList();
        }

        private void CalculatePlayerPoints(MatchResult player, ScoringRules rules)
        {
            player.KillPoints = player.Kills * rules.PointsPerKill;
            player.PlacementPoints = 0;

            // A. Puntos por Umbrales (Thresholds)
            // Ej: "Llegar al Top 10 da 5 pts"
            if (rules.Thresholds != null)
            {
                foreach (var t in rules.Thresholds)
                {
                    if (player.Rank <= t.ThresholdRank)
                    {
                        player.PlacementPoints += t.Points;
                    }
                }
            }

            // B. Puntos por Rangos Progresivos
            // Ej: "Del Top 30 al Top 20, 1 pto por cada puesto escalado"
            if (rules.Ranges != null)
            {
                foreach (var r in rules.Ranges)
                {
                    // Si el jugador quedó peor que el inicio del rango (ej quedó 50 y el rango empieza en 30), no suma.
                    if (player.Rank > r.StartRank) continue;

                    // Calculamos cuántos "pasos" dio dentro del rango.
                    // El "tope" es el rank del jugador o el fin del rango, lo que sea mayor (peor número).
                    // Ej: Rango 30->20. Jugador Rank 25.
                    // Steps = 30 - 25 + 1 = 6 puntos.
                    
                    // Si Jugador Rank 1.
                    // Steps = 30 - 20 + 1 = 11 puntos (sumó todo el rango).

                    int effectiveEnd = Math.Max(player.Rank, r.EndRank);
                    int steps = (r.StartRank - effectiveEnd) + 1;

                    if (steps > 0)
                    {
                        player.PlacementPoints += (steps * r.PointsPerStep);
                    }
                }
            }

            player.TotalPoints = player.KillPoints + player.PlacementPoints;
        }
    }
}