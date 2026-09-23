using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRepartidoresReplicados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repartidores_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreCompleto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentoIdentidad = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CiudadOperacion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FotoPerfilUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repartidores_replicados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repartidores_replicados_Activo",
                table: "repartidores_replicados",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "IX_repartidores_replicados_CiudadOperacion",
                table: "repartidores_replicados",
                column: "CiudadOperacion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repartidores_replicados");
        }
    }
}
