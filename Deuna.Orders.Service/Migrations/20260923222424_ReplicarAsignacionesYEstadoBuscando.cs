using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Orders.Service.Migrations
{
    /// <inheritdoc />
    public partial class ReplicarAsignacionesYEstadoBuscando : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedidos_asignados_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    FechaAsignacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaLlegadaLocal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaSalidaRuta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEntrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstadoAsignacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MotivoRechazo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_asignados_repartidor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_asignados_repartidor_PedidoId",
                table: "pedidos_asignados_repartidor",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_asignados_repartidor_PedidoId_RepartidorId_FechaAsi~",
                table: "pedidos_asignados_repartidor",
                columns: new[] { "PedidoId", "RepartidorId", "FechaAsignacion" },
                unique: true);

            // Los pedidos que ya existían nacieron con "Pendiente", que no pertenece a la
            // máquina de estados del flujo: el pedido nace Buscando (esperando asignación).
            // Sin esta corrección quedarían en un estado que ningún servicio reconoce.
            migrationBuilder.Sql("""
                UPDATE pedidos SET "Estado" = 'Buscando' WHERE "Estado" = 'Pendiente';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedidos_asignados_repartidor");

            // Vuelve al estado que el servicio anterior conocía. Los pedidos que ya avanzaron
            // en el flujo no se tocan: quedan en su estado real.
            migrationBuilder.Sql("""
                UPDATE pedidos SET "Estado" = 'Pendiente' WHERE "Estado" = 'Buscando';
                """);
        }
    }
}
