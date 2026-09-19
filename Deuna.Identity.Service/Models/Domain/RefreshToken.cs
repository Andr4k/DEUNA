using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Refresh tokens para autenticación JWT
/// </summary>
[Table("refresh_tokens")]
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [Required]
    public Guid UsuarioId { get; set; }

    [Required]
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public bool IsRevoked { get; set; } = false;

    [MaxLength(45)]
    public string? CreatedByIp { get; set; }

    [MaxLength(500)]
    public string? RevokedByIp { get; set; }

    [MaxLength(500)]
    public string? ReplacedByToken { get; set; }

    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }
}