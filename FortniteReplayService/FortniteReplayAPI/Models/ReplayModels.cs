using System;
using System.Collections.Generic;
using System.Text.Json.Serialization; // Movido al inicio para evitar el error CS1529
using System.ComponentModel.DataAnnotations;

namespace FortniteReplayAPI.Models
{
    /// <summary>
    /// Modelo principal que representa un archivo de Replay analizado.
    /// </summary>
    public class ReplayFile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? FileName { get; set; }
        
        public string? Hash { get; set; }

        public DateTime UploadDate { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; }

        // Relación con los datos de la partida
        public MatchData? MatchInfo { get; set; }
    }

    /// <summary>
    /// Detalles específicos de la partida.
    /// </summary>
    public class MatchData
    {
        public DateTime MatchDate { get; set; }
        
        public int DurationInMilliseconds { get; set; }
        
        public string? GameVersion { get; set; }
        
        public int TotalPlayers { get; set; }
        
        public bool IsTournament { get; set; }

        public string? PlaylistId { get; set; } // Ejemplo: 'Playlist_DefaultSolo'

        public List<PlayerStat> Players { get; set; } = new List<PlayerStat>();
        
        public List<EliminationEvent> KillFeed { get; set; } = new List<EliminationEvent>();
    }

    /// <summary>
    /// Estadísticas de un jugador individual en la partida.
    /// </summary>
    public class PlayerStat
    {
        public string? EpicId { get; set; }
        
        public string? Username { get; set; }
        
        public bool IsBot { get; set; }
        
        public int Placement { get; set; }
        
        public int Kills { get; set; }
        
        public int Assists { get; set; }
        
        public int DamageDealt { get; set; }
        
        public int DamageTaken { get; set; }
        
        public int MaterialsGathered { get; set; }
        
        public string? Platform { get; set; } // PC, PSN, XBL, etc.
    }

    /// <summary>
    /// Representa un evento de eliminación en la línea de tiempo.
    /// </summary>
    public class EliminationEvent
    {
        public int TimeOffset { get; set; } // Tiempo desde el inicio en ms
        
        public string? KillerId { get; set; }
        
        public string? VictimId { get; set; }
        
        public string? Weapon { get; set; } // Ejemplo: 'Rifle de Asalto'
        
        public bool IsHeadshot { get; set; }
        
        public float Distance { get; set; }
    }

    /// <summary>
    /// DTO para recibir la subida del archivo desde el cliente.
    /// </summary>
    public class ReplayUploadRequest
    {
        [Required]
        public string? FileName { get; set; }

        [Required]
        public byte[]? FileContent { get; set; }

        public string? UserNotes { get; set; }
    }

    /// <summary>
    /// Define las reglas para calcular el puntaje de una partida.
    /// </summary>
    public class ScoringRules
    {
        public int PointsPerKill { get; set; } = 1;
        public int PointsForWin { get; set; } = 10;
        public int PointsForTop10 { get; set; } = 5;
        public int PointsForTop25 { get; set; } = 2;

        public List<RankThreshold> Thresholds { get; set; } = new List<RankThreshold>();
        public List<RankRange> Ranges { get; set; } = new List<RankRange>();
    }

    /// <summary>
    /// Define un umbral de rango específico para otorgar puntos (ej. Top 10 = 5 puntos).
    /// </summary>
    public class RankThreshold 
    {
        public int ThresholdRank { get; set; }
        public int Points { get; set; }
    }

    /// <summary>
    /// Define un rango de posiciones para otorgar puntos progresivos.
    /// </summary>
    public class RankRange
    {
        public int StartRank { get; set; }
        public int EndRank { get; set; }
        public int PointsPerStep { get; set; }
    }

    /// <summary>
    /// Resultado calculado tras aplicar las reglas de puntuación.
    /// </summary>
    public class MatchResult
    {
        // Propiedad Id requerida por el controlador
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string? ReplayId { get; set; }
        
        public string? PlayerName { get; set; }
        public bool IsBot { get; set; }
        public int Kills { get; set; }
        public int TotalPoints { get; set; } 
        
        // Cambiado a int para solucionar error CS0029 (asignación de entero a string)
        public int Rank { get; set; } 

        public int KillPoints { get; set; }
        public int PlacementPoints { get; set; }
    }
}