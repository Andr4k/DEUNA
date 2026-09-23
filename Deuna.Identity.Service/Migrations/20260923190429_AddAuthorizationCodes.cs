using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Identity.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "codigos_autorizacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaVencimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaUso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmitidoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestauranteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notas = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_codigos_autorizacion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_autorizacion_codigo",
                table: "codigos_autorizacion",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigos_autorizacion_estado",
                table: "codigos_autorizacion",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "ix_codigos_autorizacion_restaurante",
                table: "codigos_autorizacion",
                column: "RestauranteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "codigos_autorizacion");
        }
    }
}
