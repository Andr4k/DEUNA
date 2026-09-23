using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionesRepartidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignaciones_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DistanciaMetros = table.Column<double>(type: "double precision", nullable: true),
                    FechaAsignacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaRechazo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoRechazo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asignaciones_repartidor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_repartidor_PedidoId",
                table: "asignaciones_repartidor",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_repartidor_RepartidorId",
                table: "asignaciones_repartidor",
                column: "RepartidorId");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_repartidor_RepartidorId_Estado",
                table: "asignaciones_repartidor",
                columns: new[] { "RepartidorId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignaciones_repartidor");
        }
    }
}
