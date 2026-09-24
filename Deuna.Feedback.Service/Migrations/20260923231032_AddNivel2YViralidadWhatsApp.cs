using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Feedback.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddNivel2YViralidadWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_comentarios_fotos",
                columns: table => new
                {
                    EncuestaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComentarioTexto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UrlFotoEvidencia = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_comentarios_fotos", x => x.EncuestaId);
                });

            migrationBuilder.CreateTable(
                name: "feedback_detalle_criterios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EncuestaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Criterio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Puntaje = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_detalle_criterios", x => x.Id);
                    table.CheckConstraint("CK_feedback_detalle_criterios_puntaje", "\"Puntaje\" BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "feedback_viral_whatsapp",
                columns: table => new
                {
                    EncuestaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FueCompartido = table.Column<bool>(type: "boolean", nullable: false),
                    FechaCompartido = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CodigoCompartido = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_viral_whatsapp", x => x.EncuestaId);
                });

            migrationBuilder.CreateTable(
                name: "restaurantes_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurantes_replicados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_detalle_criterios_EncuestaId_Criterio",
                table: "feedback_detalle_criterios",
                columns: new[] { "EncuestaId", "Criterio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viral_whatsapp_CodigoCompartido",
                table: "feedback_viral_whatsapp",
                column: "CodigoCompartido",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_viral_whatsapp_FueCompartido",
                table: "feedback_viral_whatsapp",
                column: "FueCompartido");

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Activo",
                table: "restaurantes_replicados",
                column: "Activo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_comentarios_fotos");

            migrationBuilder.DropTable(
                name: "feedback_detalle_criterios");

            migrationBuilder.DropTable(
                name: "feedback_viral_whatsapp");

            migrationBuilder.DropTable(
                name: "restaurantes_replicados");
        }
    }
}
