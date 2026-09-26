using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Orders.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddCalificacionesReplicadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calificaciones_replicadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CalificacionDomiciliario = table.Column<int>(type: "integer", nullable: false),
                    CalificacionRestaurante = table.Column<int>(type: "integer", nullable: false),
                    FechaCalificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calificaciones_replicadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calificaciones_replicadas_PedidoId",
                table: "calificaciones_replicadas",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calificaciones_replicadas_RepartidorId",
                table: "calificaciones_replicadas",
                column: "RepartidorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calificaciones_replicadas");
        }
    }
}
