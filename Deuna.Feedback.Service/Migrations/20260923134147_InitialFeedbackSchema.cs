using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Feedback.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialFeedbackSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_encuestas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RatingGeneralComida = table.Column<int>(type: "integer", nullable: false),
                    RatingServicioRepartidor = table.Column<int>(type: "integer", nullable: false),
                    DeseaRecomendar = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_encuestas", x => x.Id);
                    table.CheckConstraint("CK_feedback_encuestas_rating_comida", "\"RatingGeneralComida\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_feedback_encuestas_rating_repartidor", "\"RatingServicioRepartidor\" BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "pedidos_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_replicados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_encuestas_PedidoId",
                table: "feedback_encuestas",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedback_encuestas_RepartidorId",
                table: "feedback_encuestas",
                column: "RepartidorId");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_encuestas_RestauranteId",
                table: "feedback_encuestas",
                column: "RestauranteId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_replicados_Estado",
                table: "pedidos_replicados",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_replicados_PedidoId",
                table: "pedidos_replicados",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_replicados_RestauranteId",
                table: "pedidos_replicados",
                column: "RestauranteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_encuestas");

            migrationBuilder.DropTable(
                name: "pedidos_replicados");
        }
    }
}
