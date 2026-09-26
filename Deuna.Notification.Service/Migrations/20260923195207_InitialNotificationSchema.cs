using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Notification.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispositivos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Plataforma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoUsoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispositivos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notificaciones_enviadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensajeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoEvento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DestinatarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cuerpo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Proveedor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Dispositivos = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificaciones_enviadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dispositivos_Token",
                table: "dispositivos",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dispositivos_UsuarioId_Activo",
                table: "dispositivos",
                columns: new[] { "UsuarioId", "Activo" });

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_enviadas_CreatedAt",
                table: "notificaciones_enviadas",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_enviadas_DestinatarioId",
                table: "notificaciones_enviadas",
                column: "DestinatarioId");

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_enviadas_MensajeId",
                table: "notificaciones_enviadas",
                column: "MensajeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_enviadas_TipoEvento",
                table: "notificaciones_enviadas",
                column: "TipoEvento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispositivos");

            migrationBuilder.DropTable(
                name: "notificaciones_enviadas");
        }
    }
}
