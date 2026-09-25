using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRestauranteReplicado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "restaurantes_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DireccionSede = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurantes_replicados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Activo",
                table: "restaurantes_replicados",
                column: "Activo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "restaurantes_replicados");
        }
    }
}
