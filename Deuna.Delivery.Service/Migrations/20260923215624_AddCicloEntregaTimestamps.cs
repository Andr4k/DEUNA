using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddCicloEntregaTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEntrega",
                table: "asignaciones_repartidor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInicioEntrega",
                table: "asignaciones_repartidor",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaEntrega",
                table: "asignaciones_repartidor");

            migrationBuilder.DropColumn(
                name: "FechaInicioEntrega",
                table: "asignaciones_repartidor");
        }
    }
}
