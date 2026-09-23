using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeliverySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickupAddress_Street = table.Column<string>(type: "text", nullable: false),
                    PickupAddress_City = table.Column<string>(type: "text", nullable: false),
                    PickupAddress_State = table.Column<string>(type: "text", nullable: false),
                    PickupAddress_PostalCode = table.Column<string>(type: "text", nullable: false),
                    PickupAddress_Country = table.Column<string>(type: "text", nullable: false),
                    PickupAddress_Location = table.Column<Point>(type: "geometry", nullable: true),
                    DropoffAddress_Street = table.Column<string>(type: "text", nullable: false),
                    DropoffAddress_City = table.Column<string>(type: "text", nullable: false),
                    DropoffAddress_State = table.Column<string>(type: "text", nullable: false),
                    DropoffAddress_PostalCode = table.Column<string>(type: "text", nullable: false),
                    DropoffAddress_Country = table.Column<string>(type: "text", nullable: false),
                    DropoffAddress_Location = table.Column<Point>(type: "geometry", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_disponibles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QrCodigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AsignadoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_disponibles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTrackings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Location = table.Column<Point>(type: "geography (point)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatusNote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTrackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryTrackings_Deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "Deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_CourierId",
                table: "Deliveries",
                column: "CourierId");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_OrderId",
                table: "Deliveries",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Status",
                table: "Deliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrackings_DeliveryId",
                table: "DeliveryTrackings",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_disponibles_Estado",
                table: "pedidos_disponibles",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_disponibles_PedidoId",
                table: "pedidos_disponibles",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_disponibles_RestauranteId",
                table: "pedidos_disponibles",
                column: "RestauranteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryTrackings");

            migrationBuilder.DropTable(
                name: "pedidos_disponibles");

            migrationBuilder.DropTable(
                name: "Deliveries");
        }
    }
}
